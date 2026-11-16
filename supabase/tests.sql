-- Run after schema.sql as the project's administrative database connection.
-- All synthetic users and libraries are rolled back, including on a failed check.
-- The final result row is the authoritative test outcome; no user prompts are read.
begin;

create temporary table promptdesk_test_result (
  passed boolean not null,
  checks integer not null,
  detail text not null
) on commit drop;

do $$
declare
  v_original_role text := current_user;
  v_user_a uuid := gen_random_uuid();
  v_user_b uuid := gen_random_uuid();
  v_empty text := '<promptdesk version="1"><columns><col id="collection" title="Colección"/><col id="category" title="Categoría"/><col id="name" title="Nombre"/><col id="description" title="Descripción"/><col id="model" title="Modelo"/><col id="prompt" title="Prompt"/><col id="note" title="Nota"/></columns><categories/><models/><columnOrder/><categoryOrder/><disabledChoices/><records></records></promptdesk>';
  v_first text;
  v_second text;
  v_result jsonb;
  v_case record;
  v_denied boolean;
  v_count bigint;
  v_checks integer := 0;
  v_counters_before text;
  v_counters_after text;
  v_reservations_before text;
  v_reservations_after text;
  v_failure text;
begin
  begin
    select md5(coalesce(jsonb_agg(to_jsonb(c) order by c.user_id)::text, '[]')) into v_counters_before
      from public.counters c where c.user_id not in (v_user_a, v_user_b);
    select md5(coalesce(jsonb_agg(to_jsonb(r) order by r.user_id, r.request_id)::text, '[]')) into v_reservations_before
      from public.number_reservations r where r.user_id not in (v_user_a, v_user_b);

    insert into auth.users(id, aud, role, email, raw_app_meta_data, raw_user_meta_data, created_at, updated_at)
      values
      (v_user_a, 'authenticated', 'authenticated', 'promptdesk-sql-' || v_user_a::text || '@example.invalid', '{}'::jsonb, '{}'::jsonb, now(), now()),
      (v_user_b, 'authenticated', 'authenticated', 'promptdesk-sql-' || v_user_b::text || '@example.invalid', '{}'::jsonb, '{}'::jsonb, now(), now());
    v_first := replace(v_empty, '<records></records>', '<records><record id="synthetic-a"><cell key="name">Primer prompt</cell><cell key="prompt">Línea Ω&#xD;&#xA;segunda línea</cell></record></records>');
    v_second := replace(v_first, 'Primer prompt', 'Editado en notebook');

    execute 'set local role authenticated';
    perform set_config('request.jwt.claim.sub', '', true);
    perform set_config('request.jwt.claims', jsonb_build_object('sub', v_user_a::text, 'role', 'authenticated')::text, true);

    if public.promptdesk_catalog_valid(v_empty) is not true then raise exception 'Empty catalogue rejected'; end if;
    v_checks := v_checks + 1;
    if public.promptdesk_head() <> '{"revision":0,"updated_at":null}'::jsonb then raise exception 'New account head is not empty'; end if;
    v_checks := v_checks + 1;
    if public.promptdesk_read() <> '{"revision":0,"catalog_xml":null}'::jsonb then raise exception 'New account read is not empty'; end if;
    v_checks := v_checks + 1;

    v_result := public.promptdesk_write(0, v_first);
    if v_result <> '{"accepted":true,"revision":1}'::jsonb then raise exception 'Initial CAS failed: %', v_result; end if;
    v_checks := v_checks + 1;
    if public.promptdesk_read()->>'catalog_xml' is distinct from v_first then raise exception 'Read changed Unicode/newline XML bytes'; end if;
    v_checks := v_checks + 1;
    if (public.promptdesk_head()->>'revision')::bigint <> 1 or public.promptdesk_head()->>'updated_at' is null then raise exception 'Head did not expose saved revision'; end if;
    v_checks := v_checks + 1;
    if public.promptdesk_write(0, v_first) <> '{"accepted":true,"revision":1}'::jsonb then raise exception 'Lost initial response retry was not idempotent'; end if;
    v_checks := v_checks + 1;
    if public.promptdesk_write(0, v_second) <> '{"accepted":false,"revision":1}'::jsonb then raise exception 'Competing initial upload overwrote the catalogue'; end if;
    v_checks := v_checks + 1;
    if public.promptdesk_write(1, v_second) <> '{"accepted":true,"revision":2}'::jsonb then raise exception 'Expected-revision update failed'; end if;
    v_checks := v_checks + 1;
    if public.promptdesk_write(1, v_first) <> '{"accepted":false,"revision":2}'::jsonb then raise exception 'Stale writer overwrote a newer revision'; end if;
    v_checks := v_checks + 1;
    if public.promptdesk_write(2, v_second) <> '{"accepted":true,"revision":2}'::jsonb then raise exception 'Unchanged upload unnecessarily advanced revision'; end if;
    v_checks := v_checks + 1;
    if public.promptdesk_read()->>'catalog_xml' is distinct from v_second then raise exception 'Rejected CAS changed stored contents'; end if;
    v_checks := v_checks + 1;

    for v_case in select * from (values
      ('null document', null::text),
      ('malformed XML', '<promptdesk'),
      ('wrong root', '<different/>'),
      ('wrong version', replace(v_empty, 'version="1"', 'version="2"')),
      ('missing records', replace(v_empty, '<records></records>', '')),
      ('missing required column', replace(v_empty, '<col id="name" title="Nombre"/>', '')),
      ('duplicate columns', replace(v_empty, '</columns>', '<col id="name" title="Duplicate"/></columns>')),
      ('blank column title', replace(v_empty, 'title="Nombre"', 'title=""')),
      ('duplicate record IDs', replace(v_empty, '<records></records>', '<records><record id="x"/><record id="x"/></records>')),
      ('blank record ID', replace(v_empty, '<records></records>', '<records><record id="  "/></records>')),
      ('duplicate cell keys', replace(v_empty, '<records></records>', '<records><record id="x"><cell key="prompt">a</cell><cell key="prompt">b</cell></record></records>')),
      ('missing cell key', replace(v_empty, '<records></records>', '<records><record id="x"><cell>a</cell></record></records>')),
      ('DTD declaration', '<!DOCTYPE promptdesk [<!ENTITY x "expansion">]>' || v_empty),
      ('oversized ASCII', repeat('x', 10485761)),
      ('oversized UTF8', repeat('é', 5242881))
    ) as cases(label, document)
    loop
      v_denied := false;
      begin
        perform public.promptdesk_write(2, v_case.document);
      exception when invalid_parameter_value then
        v_denied := true;
      end;
      if not v_denied then raise exception 'Invalid catalogue accepted: %', v_case.label; end if;
      if public.promptdesk_read()->>'catalog_xml' is distinct from v_second
         or (public.promptdesk_head()->>'revision')::bigint <> 2 then
        raise exception 'Invalid input changed the saved catalogue: %', v_case.label;
      end if;
      v_checks := v_checks + 1;
    end loop;

    for v_case in select * from (values (null::bigint), (-1::bigint), (9223372036854775807::bigint)) as cases(revision)
    loop
      v_denied := false;
      begin perform public.promptdesk_write(v_case.revision, v_first);
      exception when invalid_parameter_value then v_denied := true; end;
      if not v_denied then raise exception 'Invalid revision accepted'; end if;
      v_checks := v_checks + 1;
    end loop;

    -- Even direct Data API calls retain validation and owner constraints.
    v_denied := false;
    begin update public.promptdesk_libraries set catalog_xml = '<different/>' where user_id = v_user_a;
    exception when invalid_parameter_value or check_violation then v_denied := true; end;
    if not v_denied then raise exception 'Direct update bypassed catalogue validation'; end if;
    v_checks := v_checks + 1;
    v_denied := false;
    begin update public.promptdesk_libraries set revision = 0 where user_id = v_user_a;
    exception when check_violation then v_denied := true; end;
    if not v_denied then raise exception 'Direct update accepted revision zero'; end if;
    v_checks := v_checks + 1;
    v_denied := false;
    begin update public.promptdesk_libraries set user_id = v_user_b where user_id = v_user_a;
    exception when insufficient_privilege then v_denied := true; end;
    if not v_denied then raise exception 'A client reassigned catalogue ownership'; end if;
    v_checks := v_checks + 1;

    perform set_config('request.jwt.claims', jsonb_build_object('sub', v_user_b::text, 'role', 'authenticated')::text, true);
    if public.promptdesk_read() <> '{"revision":0,"catalog_xml":null}'::jsonb then raise exception 'User B read user A library through RPC'; end if;
    v_checks := v_checks + 1;
    select count(*) into v_count from public.promptdesk_libraries;
    if v_count <> 0 then raise exception 'User B read user A library through table'; end if;
    v_checks := v_checks + 1;
    update public.promptdesk_libraries set catalog_xml = v_first where user_id = v_user_a;
    get diagnostics v_count = row_count;
    if v_count <> 0 then raise exception 'User B modified user A library'; end if;
    v_checks := v_checks + 1;
    v_denied := false;
    begin insert into public.promptdesk_libraries(user_id, revision, catalog_xml) values (v_user_a, 1, v_empty);
    exception when insufficient_privilege then v_denied := true; end;
    if not v_denied then raise exception 'User B inserted a library owned by user A'; end if;
    v_checks := v_checks + 1;
    if public.promptdesk_write(0, v_empty) <> '{"accepted":true,"revision":1}'::jsonb then raise exception 'User B could not create own library'; end if;
    v_checks := v_checks + 1;
    select count(*) into v_count from public.promptdesk_libraries;
    if v_count <> 1 then raise exception 'User B can see more than its own library'; end if;
    v_checks := v_checks + 1;
    v_denied := false;
    begin delete from public.promptdesk_libraries where user_id = v_user_b;
    exception when insufficient_privilege then v_denied := true; end;
    if not v_denied then raise exception 'Client role can delete the shared library row'; end if;
    v_checks := v_checks + 1;

    perform set_config('request.jwt.claims', jsonb_build_object('sub', v_user_a::text, 'role', 'authenticated')::text, true);
    if public.promptdesk_read()->>'catalog_xml' is distinct from v_second
       or (public.promptdesk_head()->>'revision')::bigint <> 2 then
      raise exception 'Another account or a rejected write changed user A library';
    end if;
    v_checks := v_checks + 1;

    perform set_config('request.jwt.claims', '{}', true);
    v_denied := false;
    begin perform public.promptdesk_head(); exception when insufficient_privilege then v_denied := true; end;
    if not v_denied then raise exception 'Head accepted authenticated role without a user identity'; end if;
    v_checks := v_checks + 1;
    v_denied := false;
    begin perform public.promptdesk_read(); exception when insufficient_privilege then v_denied := true; end;
    if not v_denied then raise exception 'Read accepted authenticated role without a user identity'; end if;
    v_checks := v_checks + 1;
    v_denied := false;
    begin perform public.promptdesk_write(0, v_empty); exception when insufficient_privilege then v_denied := true; end;
    if not v_denied then raise exception 'Write accepted authenticated role without a user identity'; end if;
    v_checks := v_checks + 1;

    execute 'set local role anon';
    v_denied := false;
    begin perform public.promptdesk_head(); exception when insufficient_privilege then v_denied := true; end;
    if not v_denied then raise exception 'Anonymous role can call head'; end if;
    v_checks := v_checks + 1;
    v_denied := false;
    begin perform public.promptdesk_read(); exception when insufficient_privilege then v_denied := true; end;
    if not v_denied then raise exception 'Anonymous role can call read'; end if;
    v_checks := v_checks + 1;
    v_denied := false;
    begin perform public.promptdesk_write(0, v_empty); exception when insufficient_privilege then v_denied := true; end;
    if not v_denied then raise exception 'Anonymous role can call write'; end if;
    v_checks := v_checks + 1;
    v_denied := false;
    begin perform 1 from public.promptdesk_libraries; exception when insufficient_privilege then v_denied := true; end;
    if not v_denied then raise exception 'Anonymous role can read the table'; end if;
    v_checks := v_checks + 1;

    execute format('set local role %I', v_original_role);
    select count(*) into v_count from pg_proc p join pg_namespace n on n.oid = p.pronamespace
      where n.nspname = 'public' and p.proname in ('promptdesk_catalog_valid','promptdesk_head','promptdesk_read','promptdesk_write')
        and not p.prosecdef and p.proconfig @> array['search_path=pg_catalog'];
    if v_count <> 4 then raise exception 'A sync function is not an invoker with a fixed search path'; end if;
    v_checks := v_checks + 1;
    if has_table_privilege('anon', 'public.promptdesk_libraries', 'SELECT')
       or has_table_privilege('authenticated', 'public.promptdesk_libraries', 'DELETE')
       or has_column_privilege('authenticated', 'public.promptdesk_libraries', 'user_id', 'UPDATE') then
      raise exception 'Unexpected table privileges';
    end if;
    v_checks := v_checks + 1;
    select md5(coalesce(jsonb_agg(to_jsonb(c) order by c.user_id)::text, '[]')) into v_counters_after
      from public.counters c where c.user_id not in (v_user_a, v_user_b);
    select md5(coalesce(jsonb_agg(to_jsonb(r) order by r.user_id, r.request_id)::text, '[]')) into v_reservations_after
      from public.number_reservations r where r.user_id not in (v_user_a, v_user_b);
    if v_counters_before is distinct from v_counters_after or v_reservations_before is distinct from v_reservations_after then
      raise exception 'PhotoBatch data changed';
    end if;
    v_checks := v_checks + 1;
    insert into pg_temp.promptdesk_test_result values (true, v_checks,
      'PASS: private accounts, revision CAS, initial-link race, retries, XML validation, permissions and unchanged PhotoBatch data.');
  exception when others then
    get stacked diagnostics v_failure = message_text;
    execute format('set local role %I', v_original_role);
    insert into pg_temp.promptdesk_test_result values (false, v_checks, SQLSTATE || ': ' || v_failure);
  end;
end;
$$;

select passed, checks, detail from pg_temp.promptdesk_test_result;
rollback;
