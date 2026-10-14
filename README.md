# Aviso Transparente

Utilidad portable para ocultar temporalmente el aviso «Activar Windows» en equipos Windows compatibles. Mantiene visible el contenido de fondo y permite restaurar el aviso.

La app es gratuita y se distribuye con licencia MIT. Las donaciones serán opcionales; el enlace de apoyo se añadirá más adelante.

## Descargar

- [Descargar AvisoTransparente.exe](https://github.com/4tuno/aviso-transparente/raw/refs/heads/main/downloads/AvisoTransparente.exe)
- [Descargar el paquete ZIP](https://github.com/4tuno/aviso-transparente/raw/refs/heads/main/downloads/AvisoTransparente.zip)

Los archivos descargables corresponden al código de este repositorio. La comprobación SHA-256 está en [downloads/SHA256SUMS.txt](downloads/SHA256SUMS.txt).

## Funciones

- Ocultar o restaurar la ventana del aviso.
- Mantenerla oculta mediante notificaciones de Windows, sin comprobaciones periódicas.
- Personalizar el atajo con Ctrl, Alt y/o Shift y una tecla.
- Guardar el atajo para la siguiente sesión.
- Trabajar desde la bandeja o usar «Ocultar y salir» para terminar el proceso después de aplicar el cambio.

## Ejecutar

Abre `AvisoTransparente.exe`. No necesita instalación ni permisos de administrador.

Para configurar el atajo, selecciona los modificadores y la tecla y pulsa **Guardar atajo**. El valor inicial es **Ctrl + Alt + F9**. Si otra aplicación ocupa la combinación, se conserva la anterior.

Cerrar normalmente restaura las ventanas que la app ocultó. **Ocultar y salir** deja el aviso oculto y cierra la app; si Windows vuelve a mostrarlo, será necesario ejecutarla de nuevo.

## Compatibilidad

Probada en Windows 11 Pro x64, compilación 26200. La detección busca la ventana específica del aviso dentro de Explorer; si no la reconoce, no modifica otras ventanas. La compatibilidad puede cambiar con una actualización de Windows.

Esta utilidad oculta un elemento visual; no activa Windows ni modifica su licencia. No elimina marcas de evaluación de Windows Insider o del modo de prueba. El ejecutable no está firmado digitalmente.

## Compilar

Requiere Windows y .NET Framework 4.8 con su compilador C#:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Compilar.ps1
```

El resultado se guarda en `dist/AvisoTransparente.exe`. El código de la interfaz y de las llamadas a Windows está en `src/AvisoTransparente.cs`. La app no ejecuta los scripts de la carpeta `cmd`.

## Usar desde CMD

La carpeta `cmd` contiene una alternativa independiente que aplica el cambio una vez y termina:

```cmd
Aviso.cmd ocultar
Aviso.cmd restaurar
Aviso.cmd estado
```

Conserva `Aviso.cmd` y `Aviso.ps1` juntos. El lanzador usa Windows PowerShell para llamar a la API de Windows.

## Datos guardados

La preferencia del atajo se guarda localmente en `%LOCALAPPDATA%\AvisoTransparente\hotkey.ini`. La app no requiere cuentas ni conexión a Internet. No agrega entradas de inicio automático.

## Reportar un problema

Incluye la versión y compilación de Windows, el estado mostrado por la app y los pasos para reproducirlo. No compartas claves de producto ni datos de tu cuenta.

## Licencia

[MIT](LICENSE). Copyright (c) 2026 4tuno.

## Referencias técnicas

- [ShowWindowAsync](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindowasync)
- [SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook)
- [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
- [Investigación de la ventana del aviso](https://gist.github.com/cw2k/26e1818303e0061e38d6a612ac52b1d5)

La implementación utiliza ocultación reversible. No utiliza la destrucción de ventanas ni la suspensión de hilos descritas en algunas variantes de esa investigación.
