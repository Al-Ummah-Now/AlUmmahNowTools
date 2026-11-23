### Which server release each page needs

Tools talks to the environment picked at sign-in (QA, stage or production). A page works there only once that
environment runs the server release below. Production runs the latest `vYYYY.MM.DD[.N]` release of
[AlUmmahNow](https://github.com/Al-Ummah-Now/AlUmmahNow/releases) that finished; QA runs `main`.
Keep this table current: a Tools PR that needs a new server route adds its row (docs/RELEASE.md).

| Page or step | Needs | First videos release |
|---|---|---|
| Sign-in, staff check (TA-10) | Auth client "Admin Tools (Desktop)" (AlUmmahNowAuth #365); admin-api `GET /api/admin/import/me` (AlUmmahNow #1715) | Auth: live since 2026-11-13; videos `v2026.11.21.5` |
| Import: channels, batches, uploads, sidecars, transcripts (TA-11) | AlUmmahNow #1715, #1719, #1720 | `v2026.11.21.5` |
| Import: error reports to the Errors page (TA-11) | AlUmmahNow #1733 | `v2026.11.22` |
| Encode on this computer, Check step (TA-12) | AlUmmahNow #1736 (renditions, `/encoding`, `/delivery`) | `v2026.11.22` |
| Transcribe: Quran verse references (TA-13) | AlUmmahNow #1745 (`GET /api/admin/import/quran-index`) | `v2026.11.22.6` |
| Translate step from imported caption files (TA-20) | AlUmmahNow #1751 | `v2026.11.22.7` |
| Imports view in videos admin (TA-8, web) | AlUmmahNow #1738 | `v2026.11.22.6` |
| Social sources (TA-13, super admin) | AlUmmahNow #1741 (not merged yet); Tools #54 | not released |
