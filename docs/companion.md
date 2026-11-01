# NX-DMX mit Bitfocus Companion und Stream Deck bedienen

NX-DMX hat drei Fernsteuer-Schnittstellen. Alle führen **normale Konsolenbefehle** aus, also genau das, was du in die
Befehlszeile tippen könntest. Damit lässt sich alles auf einen Stream-Deck-Knopf legen: auswählen, Werte und Farben
setzen, Record, Go, Blackout, Oops.

Einschalten und einstellen kannst du sie im Bereich **Remote** der Software. Dort stehen auch fertige Beispiel-URLs zum
Kopieren.

| Schnittstelle | Standardport | Companion-Verbindung |
|---|---|---|
| HTTP | 8765 | Generic: HTTP Requests |
| OSC (UDP) | 8000 | Generic: OSC |
| Text (TCP und UDP) | 7000 | Generic: TCP/UDP |

## Einrichten

1. In NX-DMX den Bereich **Remote** öffnen. Läuft Companion auf **demselben Computer**, ist nichts weiter nötig. Läuft es auf
   einem anderen Gerät, **Allow other computers** einschalten und auf **Apply** klicken. Optional ein Token für HTTP eintragen.
2. In Companion eine Verbindung **Generic: HTTP Requests** anlegen. Als Basis-URL `http://127.0.0.1:8765` eintragen (oder
   die Adresse des Pult-Rechners, sie steht im Bereich Remote).
3. Einen Knopf anlegen, als Aktion **GET** mit einem Pfad aus der Liste unten wählen.

## HTTP

Jede Anfrage antwortet mit JSON: `{"ok":true,"message":"…"}`. Bei einem Fehler lautet der Status 400, und `message` sagt, was
nicht ging (zum Beispiel „Cuelist 9 does not exist“). Es funktionieren `GET` und `POST`.

### Befehle

| Knopf | Pfad |
|---|---|
| GO Cuelist 1 | `/api/go/1` |
| BACK Cuelist 1 | `/api/back/1` |
| OFF Cuelist 1 | `/api/off/1` |
| TOGGLE Cuelist 2 | `/api/toggle/2` |
| Gehe zu Cue 5 | `/api/goto/5` oder `/api/goto/5/2` (Cuelist 2) |
| Gruppe 3 wählen | `/api/group/3` |
| Fixtures 1 bis 8 wählen | `/api/fixture/1/8` |
| Intensität 100 % | `/api/at/100` |
| Rot auf 100 % | `/api/red/100` |
| Farbe Rot | `/api/color/Red` |
| Farbe Warm White | `/api/color/Warm White` |
| Blackout umschalten | `/api/blackout` |
| Blackout ein / aus | `/api/blackout/on`, `/api/blackout/off` |
| Master 70 % | `/api/master/70` |
| Programmer leeren | `/api/clearall` |
| Oops | `/api/oops` |

### Beliebige Befehle

Alles, was die Befehlszeile versteht, geht über `command`:

```text
/api/command?line=Fixture 1 Thru 8
/api/cmd/Gobo Function "Gobo 2"
/api/cmd/Group 2; Color Blue; At 80
```

Mehrere Befehle trennst du mit `;`. Sie laufen der Reihe nach und halten beim ersten Fehler an. Per `POST` kannst du die
Zeile auch als Text oder als JSON (`{"line":"Go Cuelist 1"}`) im Body schicken.

### Zustand lesen

| Pfad | Antwort |
|---|---|
| `/api/state` | Zustand als JSON: Blackout, Master, Auswahl, Cuelists mit aktuellem Cue, Gruppen, Universen |
| `/api/vars` | Flache Liste `Name → Wert`, gut für Variablen |
| `/api/var/<name>` | Ein einzelner Wert als Text, z. B. `/api/var/master` oder `/api/var/cuelist.1.cue` |

Verfügbare Variablen: `blackout`, `master`, `selection`, `selection_count`, `programmer_values`, `fixtures`, `universes`,
`cuelist.<n>.label|active|cue|cue_label|cues`, `group.<n>.label|count`, `universe.<n>.label|enabled`.

### Sicherheit

- Ohne Token lässt NX-DMX HTTP-Anfragen von **Webseiten im Browser** nicht zu (Anfragen mit `Origin`- oder
  `Sec-Fetch-Site`-Kopfzeile), Companion sendet diese nicht.
- Mit Token (`?token=…`, Kopfzeile `X-Token: …` oder `Authorization: Bearer …`) braucht jede Anfrage außer der Hilfeseite
  `/` das Token.
- Standardmäßig hören alle Schnittstellen nur auf `127.0.0.1`. **OSC und der Textport haben kein Passwort.** Schalte
  **Allow other computers** nur in einem Netz ein, dem du vertraust.

## OSC

Verbindung **Generic: OSC** mit Host und Port 8000. Adressen beginnen mit `/nxdmx/`:

| Adresse | Wirkung |
|---|---|
| `/nxdmx/go` oder `/nxdmx/go 2` | Go für Cuelist 1 oder 2 (ebenso `back`, `off`, `toggle`) |
| `/nxdmx/group 3` oder `/nxdmx/group/3` | Gruppe wählen |
| `/nxdmx/fixture 5` | Fixture wählen |
| `/nxdmx/at 80` | Intensität |
| `/nxdmx/red 100` | Ein Attribut auf einen Wert (`green`, `blue`, `pan`, `tilt`, `zoom` …) |
| `/nxdmx/color Red` | Farbe |
| `/nxdmx/blackout` | Blackout umschalten (`1` = ein, `0` = aus) |
| `/nxdmx/master 80` | Master in Prozent |
| `/nxdmx/clear`, `/nxdmx/clearall`, `/nxdmx/oops` | Programmer |
| `/nxdmx/cmd "Group 2; Color Blue"` | Beliebiger Befehl als Text |

Ein Knopf sendet beim Drücken oft `1` und beim Loslassen `0`. Das Loslassen (`0`) führt bei `go`, `group` und ähnlichen
Adressen nichts aus.

## Text über TCP oder UDP

Verbindung **Generic: TCP/UDP** mit Port 7000. Jede Zeile ist ein Befehl, die Antwort ist `OK <Meldung>` oder
`ERR <Meldung>`:

```text
Go Cuelist 1
Fixture 1 Thru 8; Color Blue
```

## Tipps

- **Ohne Auswahl wirkt ein Wert nicht:** Wähle zuerst Fixtures oder eine Gruppe (`/api/group/1`), dann `/api/at/100`. Ein Knopf
  kann beides: `/api/cmd/Group 1; At 100`.
- **Rückmeldung:** Lies mit `/api/var/cuelist.1.active` oder `/api/var/blackout` den Zustand ab, um einen Knopf bei laufendem
  Cue leuchten zu lassen.
- **Aktivität:** Im Bereich Remote und im Befehls-Log siehst du jeden empfangenen Befehl mit Quelle (HTTP, OSC, TCP, UDP).
