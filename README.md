# PromptDesk

Aplicación de escritorio para Windows para organizar, editar y copiar prompts.
C# / WinForms / .NET Framework 4.5, sin dependencias de Python, Node ni Excel.

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

Cada publicación exige incrementar `Branding.Version` y sus atributos de versión.
Al subir a `main`, GitHub Actions compila en Windows, ejecuta pruebas y publica
una release con instalador completo, parche y `latest.json`. Si la versión ya
está publicada no se reemplaza: se debe incrementar para publicar cambios.
Los parches se generan contra la release anterior. Los saltos desde versiones
antiguas requieren un paquete acumulativo explícitamente validado.
