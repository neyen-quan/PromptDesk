# PromptDesk

Aplicación de escritorio para Windows para organizar, editar y copiar prompts.
C# / WinForms / .NET Framework 4.5, sin dependencias de Python, Node ni Excel.

## Sincronización entre equipos · 1.3.0

Usa **Cuenta → Iniciar sesión** con el mismo correo y contraseña de PhotoBatch.
PromptDesk comparte el proyecto **Programas 404**, pero guarda su biblioteca en
una tabla privada distinta de los contadores. No hace falta otra cuenta.

- La primera vinculación une los prompts locales y los que ya estén en la nube.
  Podés empezar en la notebook y vincular después la PC con tu biblioteca anterior.
- Sincroniza prompts, favoritos, variables, columnas, categorías, modelos y orden.
- Guarda automáticamente y muestra el estado junto al botón de cuenta. Antes de
  cambiar de equipo, esperá **Sincronizado** o usá **Sincronizar ahora**.
- Combina cambios independientes. Si dos equipos editan distinto el mismo campo,
  conserva ambas versiones y avisa; una versión vieja no sobrescribe a la nueva.
- Ante un corte de conexión conserva los cambios locales pendientes. Al cerrar
  intenta enviarlos y avisa si todavía faltan cambios por sincronizar.

En una PC con datos anteriores, actualizá el programa antes de iniciar sesión.
No copies archivos de sesión entre equipos ni reemplaces datos.xml manualmente.
Cada instalación queda vinculada a una cuenta para evitar subir una biblioteca
personal por error a una cuenta diferente. Cerrar sesión no borra los prompts.

## Biblioteca renovada

La vista principal organiza el trabajo en tres zonas: colecciones y categorías,
lista de prompts con extractos, y editor. El nombre, la categoría y el modelo se
pueden cambiar desde el editor. **Copiar prompt** es la acción principal; las
herramientas menos frecuentes están agrupadas en menús.

- Buscar por varias palabras en nombre, texto y otros campos, sin distinguir tildes o mayúsculas.
- Acceder a todos los prompts, favoritos y recientes.
- Elegí una categoría y usá **+ → Nueva subcarpeta** para crear carpetas dentro de ella. Podés anidarlas, crear prompts en la carpeta elegida o mover uno desde el desplegable **Categoría** de su ficha.
- Archivar desde **Más** y consultar el histórico en **Archivados**. Restaurar devuelve el prompt a su colección y categoría con sus variables y favorito. El atajo global queda en pausa durante el archivo.
- Usar **Lista** para recorrer contenido y **Tabla** para editar columnas personalizadas.
- Guardado automático, deshacer/rehacer, variables al copiar y atajos globales.
- En **Variables**, seleccioná el marcador y abrí **Editar opciones…**. Guardá un nombre y el texto de reemplazo de cada alternativa. Al copiar, **Libre** es el primer botón y permite escribir. Los demás botones usan los textos guardados. Arrastralos para cambiar su orden. Cada variable recuerda el orden y la última opción que confirmaste al copiar.
- Mantener categorías, modelos, orden y campos existentes sin cambiar el formato del catálogo.

El diseño toma como referencias de organización a
[SnippetsLab](https://www.renfei.org/snippets-lab/),
[Raycast Snippets](https://manual.raycast.com/snippets) y
[TypingMind Prompt Library](https://custom.typingmind.com/features/prompt-library).
La interfaz y los controles se implementan localmente; no requieren esos servicios.

## Instalar

En [Releases](https://github.com/neyen-quan/PromptDesk/releases/latest), descargar
`PromptDesk_Windows.zip`, extraer y abrir `INSTALAR_O_ACTUALIZAR.vbs`.
El ejecutable se instala en `%LOCALAPPDATA%\Programs\PromptDesk`.
También admite uso portable desde la carpeta extraída.

## Actualizar

Las actualizaciones se aplican directamente en la PC. Cerrá PromptDesk para
instalar la compilación probada en su carpeta de ejecución. La biblioteca y
los respaldos se guardan por separado. Para sincronizar las subcarpetas entre
la PC y la notebook, usá 1.3.5 o posterior en ambos equipos.

## Datos personales

El catálogo se guarda en `%LOCALAPPDATA%\PromptDesk\datos.xml` y su historial
junto a él. Se mantiene separado del programa y nunca se sube al repositorio.
La distribución pública incluye el esquema vacío; no incluye el Excel ni
prompts personales o materiales de cursos.

## Desarrollo

Código en `app/src`, identidad visual en `app/assets`, motor de actualización
en `app/UpdatePromptDesk.ps1`. Compilar en Windows con `app/Preparar.ps1`.
Las pruebas usan registros sintéticos creados únicamente en una carpeta temporal.

Las responsabilidades se separan entre almacenamiento (`Core.cs`), Excel
(`Excel.cs`), sincronización (`CloudClient.cs`, `CloudMerge.cs`, `CloudVault.cs`,
`MainWindow.Cloud.cs`), búsqueda (`LibraryQuery.cs`), historial (`History.cs`), comportamiento
de la ventana (`App.cs`) y presentación (`MainWindow.Layout.cs`, `LibraryControls.cs`,
`Theme.cs`). La compilación incluye todos los archivos C# de `app/src`.

`PromptDesk.exe --test` comprueba los datos y operaciones del catálogo.
`PromptDesk.exe --ui-test CARPETA` usa un catálogo sintético, comprueba la interfaz
y genera capturas de escritorio/notebook. No carga ni modifica la biblioteca real.
Las pruebas de nube usan un transporte simulado; nunca abren una sesión real.
En Windows se comprueba también el cifrado DPAPI de las credenciales de sesión.
Las pruebas Windows también aplican el parche con el actualizador incluido en la
versión publicada anterior y comparan el resultado con el paquete completo.

Cada publicación exige incrementar `Branding.Version` y sus atributos de versión.
Los cambios se guardan en GitHub como respaldo del código. Para una publicación
explícita, el workflow manual compila en Windows, ejecuta pruebas y publica
una release con instalador completo, parche y `latest.json`. Si la versión ya
está publicada no se reemplaza: se debe incrementar para publicar cambios.
Los parches son acumulativos desde 1.1.3: cada base publicada compatible se
descarga y se valida con su propio actualizador antes de publicar.

La publicación se prepara como borrador y se descargan sus archivos para verificar
SHA-256 antes de activar el canal estable. Se adjuntan checksums del programa completo
y del parche. La copia antigua del proyecto que estaba duplicada dentro del repositorio
se conserva en el historial de Git; las fuentes activas están únicamente en `app` y `scripts`.

## Base compartida

`supabase/schema.sql` define exclusivamente los objetos `promptdesk_*`: una
biblioteca XML por usuario, RLS por propietario y escrituras con revisión esperada
(CAS). `supabase/tests.sql` comprueba aislamiento entre cuentas, conflictos,
reintentos y validación; todos sus usuarios sintéticos se revierten con ROLLBACK.
No modifica las tablas ni los contadores de PhotoBatch. El límite por biblioteca
es 10 MiB; las imágenes y videos no se suben desde PromptDesk.

El cliente incluye solo la clave pública publicable. El acceso real exige la
sesión del usuario. Los tokens se guardan cifrados para el usuario de Windows
actual; la contraseña no se conserva. Cada equipo inicia su propia sesión y
cerrar sesión aquí no cierra PhotoBatch ni otros equipos.
