-- PromptDesk library sync inside the shared Programas 404 project.
-- PhotoBatch counters and number-reservation functions are deliberately unrelated.
-- Clients merge against their last confirmed snapshot before calling the CAS RPC.

create function public.promptdesk_catalog_valid(p_catalog_xml text)
returns boolean
language plpgsql
immutable
strict
security invoker
set search_path = pg_catalog
as $$
declare
  v_document xml;
  v_columns text[];
  v_column_count bigint;
  v_distinct_columns bigint;
begin
  if octet_length(p_catalog_xml) > 10485760 then
    raise exception using errcode = '22023', message = 'La biblioteca supera el límite de 10 MiB.';
  end if;
  -- Reject declarations before invoking libxml. The desktop parser also prohibits DTDs.
  if position('<!DOCTYPE' in p_catalog_xml) > 0 or position('<!ENTITY' in p_catalog_xml) > 0 then
    raise exception using errcode = '22023', message = 'La biblioteca no admite DTD ni entidades externas.';
  end if;
  if not xml_is_well_formed_document(p_catalog_xml) then
    raise exception using errcode = '22023', message = 'La biblioteca no contiene un documento XML válido.';
  end if;
  v_document := xmlparse(document p_catalog_xml);
  if not xpath_exists('/promptdesk[@version="1" and count(columns)=1 and count(records)=1]', v_document) then
    raise exception using errcode = '22023', message = 'El catálogo de PromptDesk está incompleto o usa otra versión.';
  end if;
  if xpath_exists('/promptdesk/columns/col[not(@id) or string-length(@id)=0 or not(@title) or string-length(@title)=0]', v_document) then
    raise exception using errcode = '22023', message = 'La biblioteca contiene columnas inválidas.';
  end if;
  select array_agg(c.id), count(*), count(distinct c.id)
    into v_columns, v_column_count, v_distinct_columns
    from xmltable('/promptdesk/columns/col' passing v_document columns id text path '@id') c;
  if v_column_count <> v_distinct_columns or not
    (array['collection','category','name','description','model','prompt','note']::text[] <@ coalesce(v_columns, array[]::text[])) then
    raise exception using errcode = '22023', message = 'Faltan columnas base o hay columnas repetidas.';
  end if;
  if xpath_exists('/promptdesk/records/record[not(@id) or string-length(normalize-space(@id))=0]', v_document)
     or exists (
       select r.id
       from xmltable('/promptdesk/records/record' passing v_document columns id text path '@id') r
       group by r.id having count(*) > 1
     ) then
    raise exception using errcode = '22023', message = 'La biblioteca contiene identificadores de prompts inválidos o repetidos.';
  end if;
  if xpath_exists('/promptdesk/records/record/cell[not(@key) or string-length(@key)=0]', v_document)
     or exists (
       select r.row_number, c.key
       from xmltable('/promptdesk/records/record' passing v_document
         columns row_number for ordinality, record_xml xml path '.') r
       cross join lateral xmltable('/record/cell' passing r.record_xml columns key text path '@key') c
       group by r.row_number, c.key having count(*) > 1
     ) then
    raise exception using errcode = '22023', message = 'Un prompt contiene campos inválidos o repetidos.';
  end if;
  return true;
end;
$$;

create table public.promptdesk_libraries (
  user_id uuid primary key references auth.users(id) on delete cascade,
  revision bigint not null check (revision > 0),
  catalog_xml text not null check (public.promptdesk_catalog_valid(catalog_xml)),
  updated_at timestamptz not null default now()
);

alter table public.promptdesk_libraries enable row level security;
alter table public.promptdesk_libraries force row level security;

create policy promptdesk_owner_read on public.promptdesk_libraries
  for select to authenticated using ((select auth.uid()) = user_id);
create policy promptdesk_owner_insert on public.promptdesk_libraries
  for insert to authenticated with check ((select auth.uid()) = user_id);
create policy promptdesk_owner_update on public.promptdesk_libraries
  for update to authenticated
  using ((select auth.uid()) = user_id)
  with check ((select auth.uid()) = user_id);

revoke all on table public.promptdesk_libraries from public, anon, authenticated;
grant select, insert on table public.promptdesk_libraries to authenticated;
grant update (revision, catalog_xml, updated_at) on public.promptdesk_libraries to authenticated;

create function public.promptdesk_head()
returns jsonb
language plpgsql
stable
security invoker
set search_path = pg_catalog
as $$
declare
  v_user uuid := auth.uid();
  v_result jsonb;
begin
  if v_user is null then
    raise exception using errcode = '42501', message = 'Iniciá sesión para sincronizar PromptDesk.';
  end if;
  select jsonb_build_object('revision', l.revision, 'updated_at', l.updated_at)
    into v_result from public.promptdesk_libraries l where l.user_id = v_user;
  return coalesce(v_result, jsonb_build_object('revision', 0, 'updated_at', null));
end;
$$;

create function public.promptdesk_read()
returns jsonb
language plpgsql
stable
security invoker
set search_path = pg_catalog
as $$
declare
  v_user uuid := auth.uid();
  v_result jsonb;
begin
  if v_user is null then
    raise exception using errcode = '42501', message = 'Iniciá sesión para sincronizar PromptDesk.';
  end if;
  select jsonb_build_object('revision', l.revision, 'catalog_xml', l.catalog_xml)
    into v_result from public.promptdesk_libraries l where l.user_id = v_user;
  return coalesce(v_result, jsonb_build_object('revision', 0, 'catalog_xml', null));
end;
$$;

create function public.promptdesk_write(p_expected_revision bigint, p_catalog_xml text)
returns jsonb
language plpgsql
volatile
security invoker
set search_path = pg_catalog
as $$
declare
  v_user uuid := auth.uid();
  v_revision bigint;
  v_current_xml text;
begin
  if v_user is null then
    raise exception using errcode = '42501', message = 'Iniciá sesión para sincronizar PromptDesk.';
  end if;
  if p_expected_revision is null or p_expected_revision < 0 or p_expected_revision >= 9223372036854775807 then
    raise exception using errcode = '22023', message = 'La revisión de sincronización no es válida.';
  end if;
  if p_catalog_xml is null then
    raise exception using errcode = '22023', message = 'No se recibió una biblioteca para sincronizar.';
  end if;
  perform public.promptdesk_catalog_valid(p_catalog_xml);

  if p_expected_revision = 0 then
    insert into public.promptdesk_libraries(user_id, revision, catalog_xml)
      values (v_user, 1, p_catalog_xml)
      on conflict (user_id) do nothing
      returning revision into v_revision;
  else
    update public.promptdesk_libraries l
      set revision = l.revision + 1, catalog_xml = p_catalog_xml, updated_at = clock_timestamp()
      where l.user_id = v_user and l.revision = p_expected_revision and l.catalog_xml <> p_catalog_xml
      returning l.revision into v_revision;
  end if;
  if v_revision is not null then
    return jsonb_build_object('accepted', true, 'revision', v_revision);
  end if;

  -- A lost HTTP response can be retried safely without another revision.
  -- Different data with an obsolete expected revision is never written.
  select l.revision, l.catalog_xml into v_revision, v_current_xml
    from public.promptdesk_libraries l where l.user_id = v_user;
  return jsonb_build_object('accepted', coalesce(v_current_xml = p_catalog_xml, false),
                           'revision', coalesce(v_revision, 0));
end;
$$;

revoke all on function public.promptdesk_catalog_valid(text) from public, anon, authenticated;
revoke all on function public.promptdesk_head() from public, anon, authenticated;
revoke all on function public.promptdesk_read() from public, anon, authenticated;
revoke all on function public.promptdesk_write(bigint, text) from public, anon, authenticated;
grant execute on function public.promptdesk_catalog_valid(text) to authenticated;
grant execute on function public.promptdesk_head() to authenticated;
grant execute on function public.promptdesk_read() to authenticated;
grant execute on function public.promptdesk_write(bigint, text) to authenticated;

comment on table public.promptdesk_libraries is
  'One private PromptDesk catalogue per account. Revision CAS prevents overwriting newer device changes.';
comment on function public.promptdesk_head() is
  'Cheap revision check. Does not download the catalogue XML.';
comment on function public.promptdesk_write(bigint, text) is
  'Compare-and-swap snapshot. On accepted=false fetch, merge locally, and retry the returned revision.';

notify pgrst, 'reload schema';
