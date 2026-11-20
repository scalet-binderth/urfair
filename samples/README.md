# Sample activity lists

CSV files (UTF-8, separator `;`) for trying the Excel/CSV import in the organisation area
(vote → "Veranstaltungen" → "Aus Excel/CSV importieren"). They open directly in Excel.

| File | Content | Try it with |
|---|---|---|
| `firmvorbereitung.csv` | 21 projects of the confirmation preparation, green and blue groups | any vote with the groups "Grüne Projekte" and "Blaue Projekte" (e.g. a new vote with sample data) |
| `firmvorbereitung-zusatz.csv` | 2 extra projects (one green, one blue) | Firmvorbereitung demo, "add" |
| `pfarrfest.csv` | 10 helper posts with shifts | Pfarrfest or any vote without groups |
| `ministrantenausflug.csv`, `ferienprogramm.csv`, `jungschar-sommerlager.csv` | the other demo votes | any vote without groups |
| `teilnehmende-beispiel.csv` | 5 invented participants (tab "Teilnehmende" → "Aus Excel/CSV importieren") | any vote |
| `beispiel-mit-fehlern.csv` | deliberate mistakes (date, title, group, seats, participation) | any vote, to show the error messages |

The lists are exports of the demo data (`GET /api/admin/slots/{id}/activities.csv`); regenerate them the same way
after changing the demo data.
