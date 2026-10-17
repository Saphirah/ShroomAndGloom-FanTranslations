# Shroom and Gloom – Deutsch

Inoffizielle deutsche Übersetzungsmod für **Windows x64 · Steam-Build 25442225**.

## Installation

1. [Neuesten Installer herunterladen](https://github.com/Saphirah/ShroomAndGloomGermanTranslation/releases/latest).
2. Spiel schließen, Installer öffnen und **Installieren** wählen.
3. Über **Spiel starten** oder Steam spielen. Deutsch ist automatisch aktiv.

Beim ersten Spielstart benötigt der Modloader eine Internetverbindung zur Einrichtung.
Deinstallation über denselben Installer. Die Version ist ein MVP; die vollständige
visuelle Prüfung steht noch aus.

## Entwicklung

Voraussetzungen: Windows, .NET SDK 8+, Python 3 und die unterstützte Spielversion.

```powershell
./tools/build.ps1 -GameDir 'E:\SteamLibrary\steamapps\common\Shroom and Gloom'
```

Eigener Code: [MIT](LICENSE). Rechte an Spieltexten und Drittkomponenten:
[Lizenzhinweise](docs/THIRD-PARTY.txt).
