# Shroom and Gloom – Deutsch

Inoffizielle deutsche Übersetzungsmod für **Steam-Build 25442225**, Windows x64,
Unity **2022.3.62f3 / IL2CPP**. Ziel: ein vollständig übersetztes MVP mit kleinem,
automatisch erkennendem EXE-Installer. Nach der Installation kann das Spiel über
einen separaten Klick auf **Spiel starten** über Steam geöffnet werden.

## Installation

Den EXE-Installer aus dem [neuesten Release](https://github.com/Saphirah/ShroomAndGloomGermanTranslation/releases/latest)
herunterladen und öffnen, erkannten Steam-Spielordner prüfen
und **Installieren** wählen. Nach erfolgreicher Installation wird der Button zu
**Spiel starten**. Erst ein weiterer Klick öffnet das Spiel über Steam; der Installer
schließt sich, sobald der Spielprozess läuft. Falls Steam den Start nicht freigibt,
bleibt der Installer offen und ermöglicht einen erneuten Versuch.
Über **Ordner wählen** sind auch andere Bibliotheken
möglich. Steam-Pfade werden über Registry, `libraryfolders.vdf`, Appmanifest und
übliche Ordner auf lokalen Festplatten gesucht.

Beim nächsten selbst gestarteten Spiel ist Deutsch aktiv. BepInEx bereitet beim
ersten Start seine IL2CPP-Schnittstellen vor und lädt passende Unity-Bibliotheken
herunter. Dafür sind einmalig Internet und etwa eine Minute Geduld erforderlich.
Das Spiel muss zum Installieren und Deinstallieren geschlossen sein.

Deinstallation über denselben Installer. Originale Spieldateien und Spielstände
werden nicht geändert. Der Installer protokolliert eigene Dateien, schützt fremde
Loader, entfernt keine nachträglich veränderten Dateien und behält den gemeinsamen
Loader bei weiteren erkannten DLL-Plugins bei. Generierte Loader-Caches bleiben
gegebenenfalls zurück.

## Umfang und tatsächlicher Prüfstand

- Alle **2.857** Einträge der englischen Tabellen redaktionell mit der vorhandenen
  deutschen Fassung verglichen: Kartennamen, Regeln, Gegner, Welt und Oberfläche.
- **540** Ergänzungen/Korrekturen gegenüber der mitgelieferten deutschen Fassung.
- Zusätzlich **12** ausschließlich deutsche Einträge/Formen geprüft und aufgenommen,
  darunter zuvor leere Dativformen. Insgesamt **2.869** Katalogeinträge.
- Spieltokens vollständig geprüft; bewusst leere technische `_`-Marker erhalten.
  Namen, Credits und nichtsprachliche Platzhalter werden gezielt beibehalten.
- Plugin gegen offline aus diesem Build erzeugte IL2CPP-Schnittstellen kompiliert.
- Echter EXE-Installer: Pfaderkennung, Installation, Wiederholungsinstallation,
  Prüfsummen, Deinstallation und Schutz fremder Loader im isolierten Ordner geprüft.
- Die Integration der fünf Tabellen wurde anhand eines echten Spielprotokolls
  bestätigt. Rückmeldungen aus dem Spiel führten zu den UI- und Layoutkorrekturen
  in 0.1.1/0.1.2. Eine vollständige Sichtprüfung dieser jüngsten Änderungen steht
  noch aus. Wegen weitgehend entfernter Unity-Typbäume sind weitere hart codierte
  Texte oder Text in Bildern nicht umfassend ausgeschlossen.

Die Version ist ein MVP für die angegebene Spielversion. Die vollständige
visuelle Abnahme steht noch aus.

## Aufbau

- `data/translations.de.json`: vollständiger Laufzeitkatalog, adressiert über
  Tabelle und 64-Bit-ID; IDs sind JSON-Zeichenketten.
- `data/editorial-*.json`: einzelne redaktionelle Entscheidungen zum fixierten
  Ausgangsexport. Der Build verweigert veränderte Quellen statt Zeilen falsch
  zuzuordnen.
- `data/supplemental.de.json`: zusätzliche deutsche Formen und ältere Einträge.
- `src/ShroomGerman`: BepInEx-6-Plugin; ergänzt Deutsch in der vorhandenen
  Sprachauswahl und überschreibt ausschließlich deutsche Tabellen im Speicher.
- `src/Installer`: selbstständiger Windows-Installer mit gebündeltem .NET-Runtime.
- `tools/InteropPrep`: reine Offline-Analyse; kein Laden/Ausführen der Spiel-EXE.
- `tools/build.ps1`: Export, Katalogprüfung, Offline-Referenzen und beide Builds.
- `tools/smoke_installer.py`: kleine Installer-Integrationsprüfung ohne Spielstart.

Die Mod prüft den unterstützten `GameAssembly.dll`-Fingerabdruck. Nach einem
inkompatiblen Code-Update wird sie deaktiviert, damit keine unpassenden Regeltexte
eingesetzt werden. Der Installer verweigert bislang unbekannte Code-Builds ebenfalls.

## Erneut bauen

Voraussetzungen: Windows x64, .NET SDK 8 oder neuer, Python 3 und die unterstützte
rechtmäßig installierte Spielversion. Keine systemweite Python-Installation von
Zusatzpaketen erforderlich.

```powershell
./tools/build.ps1 -GameDir 'E:\SteamLibrary\steamapps\common\Shroom and Gloom'
python tools/smoke_installer.py --game 'E:\SteamLibrary\steamapps\common\Shroom and Gloom'
```

Downloads werden lokal in `.tools/` abgelegt; der BepInEx-Download ist auf Version
788 und SHA-256 festgelegt. Generierte Interop-DLLs und Originalbundles gehören
nicht ins Installerpaket und nicht ins Git-Repository. Die eigenen Prüfergebnisse
liegen in `artifacts/`.

## Redaktionelle Regeln

- Schmiere = Gunk; Glibber = Goo; Finsternis = Gloom; Knuspi = Toasty.
- Erkundungskarte / Kampfkarte; Gesundheit / GP; Energie; Akku; Wut; Verfall.
- Lähmen / Gelähmt konsistent; Fleckenfrei für Stainless; Feststeckend für Lodged.
- Bleibend für Persistent; Permanent für dauerhafte Deckzugehörigkeit; diese
  verschiedenen Spielmechaniken dürfen nicht zusammengelegt werden.
- Regeltexte gegenüber dem englischen Ausgangstext prüfen, insbesondere Auslöser,
  Ziele, Mengen, Multiplikation, Kosten, Zeitpunkt und Dauer.
- Technische Linkziele, Farbmarker und Variablen unverändert erhalten. JOINER-Tokens
  dürfen für die deutsche Satzstellung durch ausgeschriebene Präpositionen ersetzt
  werden; diese sind ausschließlich sprachliche Verbinder.
- Reine Entwicklerplatzhalter übersetzen, ohne neue Spielinhalte zu erfinden.
- Die mitgelieferten deutschen Credits waren älter: aktuelle Namensliste aus der
  englischen Tabelle übernehmen und nur Rubriken übersetzen.

## Nächste Spielprüfung

Für die nächste Spielprüfung: Modlog auf **alle fünf
Tabellen** und **Deutsch aktiv** prüfen; Hauptmenü, je eine Kampf-/Erkundungskarte,
Tooltip, Sprachwechsel sowie Neustart kurz ansehen. Anschließend auffällige
Satzkombinationen und Textüberläufe gezielt korrigieren. Keine vollständige
automatisierte Spieltest-Suite vorgesehen.

Quellen und Lizenzhinweise: `docs/THIRD-PARTY.txt`, `docs/licenses/`.
Der eigene Programmcode steht unter der [MIT-Lizenz](LICENSE). Spieltexte,
vorhandene Übersetzungen und Drittkomponenten bleiben unter den Rechten und
Lizenzbedingungen ihrer jeweiligen Rechteinhaber; siehe die Hinweise oben.

## Update 0.1.1

- Fehlende direkte UI-Beschriftungen wie TAKE IT über die tatsächlichen TMP- und
  LocalizedFallbackText-Anzeigepfade übersetzen.
- Deutsche Pausenmenü-Buttons passend verkleinern; FORTSETZEN statt PAUSE BEENDEN.
- Äußere Leerzeichen, Tabs und Zeilenumbrüche exakt wie im englischen Ausgangstext
  erhalten; explizite Wortabstände bei zusammengesetzten Kartenfragmenten.
- Änderungen kompiliert und Katalog statisch geprüft. Die Spielprüfung übernimmt
  der Nutzer; die laufende Spielinstanz wird nicht verändert.

## Update 0.1.2

TMP Auto Size für die tatsächliche `_typeText`-Beschriftung von normalen und
Voraussicht-Karten sowie die Pausenmenü-Aktionen. Unterstützt sowohl Canvas-TMP
als auch 3D-TextMeshPro. Zeilenumbruch ist für diese kurzen Labels deaktiviert;
Min/Max-Schriftgrößen erlauben Verkleinerung auch bei kleinen Weltkoordinaten.
Textcontainer bleiben auf ihrer vorhandenen Fläche begrenzt. Spielprüfung durch
den Nutzer; kein automatischer Spielstart oder Eingriff in die laufende Instanz.
