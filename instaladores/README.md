# Instaladores para el pack inicial

Estos archivos se guardan localmente en esta carpeta. El repositorio público solo contiene este inventario; descarga los binarios de sus proveedores antes de continuar en otro equipo. LibreOffice queda fuera del pack por ahora.

| Aplicación | Archivo | SHA-256 | Descarga |
| --- | --- | --- | --- |
| Adobe Reader | `AcroRdrDC2600221931_es_ES.exe` | `f71b21c3ecff0d742949d945b70d4f5c4cb0ab9be99d590b1ffe5a498c0853fc` | [Adobe](https://ardownload3.adobe.com/pub/adobe/reader/win/AcrobatDC/2600221931/AcroRdrDC2600221931_es_ES.exe) |
| IZArc | `IZArc_4.6.exe` | `6f330c4ffc4c9c268fe301246d5465b7e4852c1152666df4e38cd8dc8f112c0e` | [IZArc](https://www.izarc.org/downloads/IZArc_4.6.exe) |
| K-Lite Codec Pack Standard | `K-Lite_Codec_Pack_2000_Standard.exe` | `1cc18f454a836ae59db6305ae08bd8b0001ba5ecc5679a60841dd5d794a2ce52` | [Codec Guide](https://files2.codecguide.com/K-Lite_Codec_Pack_2000_Standard.exe) |
| Google Chrome Enterprise | `googlechromestandaloneenterprise64.msi` | `573e4ca45171abb5a4c3cd1d8b198dbda93101561e44d720f8a166fae1dbd747` | [Chrome Enterprise](https://chromeenterprise.google/download/) |
| VLC | `vlc-3.0.24-win64.exe` | `d711e1e1fe52052748c39080c7dce63f6b7e4c315efedf1774a3f1957b782ff3` | [VideoLAN](https://www.videolan.org/vlc/download-windows.html) |

Los archivos se identificaron y verificaron estáticamente el 2 de octubre de 2026. Chrome, VLC y Adobe tenían firma Authenticode válida. IZArc y K-Lite no estaban firmados; el SHA-256 de K-Lite coincidía con el publicado por Codec Guide. Ninguno se ha instalado como parte de esta comprobación. Antes de habilitar el pack por defecto, probar la instalación desatendida en una VM Windows limpia.
