# PromptDesk

Aplicación de escritorio para Windows para organizar, editar y copiar prompts.
C# / WinForms / .NET Framework 4.5, sin dependencias de Python, Node ni Excel.

## Biblioteca renovada · 1.2.1

La vista principal organiza el trabajo en tres zonas: colecciones y categorías,
lista de prompts con extractos, y editor. El nombre, la categoría y el modelo se
pueden cambiar desde el editor. **Copiar prompt** es la acción principal; las
herramientas menos frecuentes están agrupadas en menús.

- Buscar por varias palabras en nombre, texto y otros campos, sin distinguir tildes o mayúsculas.
- Acceder a todos los prompts, favoritos y recientes.
- Usar **Lista** para recorrer contenido y **Tabla** para editar columnas personalizadas.
- Guardado automático, deshacer/rehacer, variables al copiar y atajos globales.
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

Desde el programa: **Actualizar → Buscar actualizaciones**.
Canal estable:

```
https://github.com/neyen-quan/PromptDesk/releases/latest/download/latest.json
```

Los paquetes de parche incluyen solo runtime modificado. El programa descarga,
verifica el ZIP y los hashes, solicita confirmación, guarda y reinicia.
Se conservan los archivos anteriores para recuperar errores de aplicación.
Los hashes comprueban integridad; los binarios aún no tienen firma de código.

Para conectar una instalación 1.1.1, pegar el canal en **Configurar canal**.
La versión 1.1.2 y posteriores lo usan por defecto.

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
(`Excel.cs`), búsqueda (`LibraryQuery.cs`), historial (`History.cs`), comportamiento
de la ventana (`App.cs`) y presentación (`MainWindow.Layout.cs`, `LibraryControls.cs`,
`Theme.cs`). La compilación incluye todos los archivos C# de `app/src`.

`PromptDesk.exe --test` comprueba los datos y operaciones del catálogo.
`PromptDesk.exe --ui-test CARPETA` usa un catálogo sintético, comprueba la interfaz
y genera capturas de escritorio/notebook. No carga ni modifica la biblioteca real.
Las pruebas Windows también aplican el parche con el actualizador incluido en la
versión publicada anterior y comparan el resultado con el paquete completo.

Cada publicación exige incrementar `Branding.Version` y sus atributos de versión.
Al subir a `main`, GitHub Actions compila en Windows, ejecuta pruebas y publica
una release con instalador completo, parche y `latest.json`. Si la versión ya
está publicada no se reemplaza: se debe incrementar para publicar cambios.
Los parches son acumulativos desde 1.1.3: cada base publicada compatible se
descarga y se valida con su propio actualizador antes de publicar.

La publicación se prepara como borrador y se descargan sus archivos para verificar
SHA-256 antes de activar el canal estable. Se adjuntan checksums del programa completo
y del parche. La copia antigua del proyecto que estaba duplicada dentro del repositorio
se conserva en el historial de Git; las fuentes activas están únicamente en `app` y `scripts`.
