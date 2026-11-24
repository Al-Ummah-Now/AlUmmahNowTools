### Which server release each page needs

Tools talks to the environment picked at sign-in (QA, stage or production). A page works there only once that
environment runs the server release below. Production runs the latest `vYYYY.MM.DD[.N]` tag of
[AlUmmahNow](https://github.com/Al-Ummah-Now/AlUmmahNow/tags) whose release run finished; QA runs `main`.
On an older server the Social sources page says it isn't there yet; an import step fails with its reason and Retry.
Keep this table current: a Tools PR that needs a new server route adds its row (docs/RELEASE.md).

| Page or step | Needs | First videos release |
|---|---|---|
| Sign-in, staff check (TA-10) | Auth client "Admin Tools (Desktop)" (AlUmmahNowAuth #470); admin-api `GET /api/admin/import/me` (AlUmmahNow #1715) | Auth: live since 2026-11-21; videos `v2026.11.21.5` |
| Import: channels, batches, uploads, sidecars, transcripts (TA-11) | AlUmmahNow #1715, #1719, #1720 | `v2026.11.21.5` |
| Import: error reports to the Errors page (TA-11) | AlUmmahNow #1733 | `v2026.11.22` |
| Encode on this computer, Check step (TA-12) | AlUmmahNow #1736 (renditions, `/encoding`, `/delivery`) | `v2026.11.22` |
| Transcribe: Quran verse references (TA-13) | AlUmmahNow #1745 (`GET /api/admin/import/quran-index`) | `v2026.11.22.6` |
| Translate step from imported caption files (TA-20) | AlUmmahNow #1751 | `v2026.11.22.7` |
| Imports view in videos admin (TA-8, web) | AlUmmahNow #1738 | `v2026.11.22.6` |
| Social sources: list, add, sync, schedule, turn off, remove (TA-13, super admin) | AlUmmahNow #1741 (`/api/admin/import/sources`) | `v2026.11.24` |
| Social sources: Cancel sync, YouTube Takeout files (TA-13) | AlUmmahNow #1752 (`/sources/:id/cancel`, `/sources/:id/takeout`) | `v2026.11.24` |
| Social sources: sync job state, log and Retry (TA-13) | `GET /api/content/jobs/:id` and its `/retry`; the attempts and log in them are AlUmmahNow #1738 | `v2026.11.22.6` |
| Social sources: Sync now and uploads hidden while uploads are off (TA-13) | AlUmmahNow #1755 (`GET /api/features`), #1756 (`404 feature_off`); without them uploads count as on | `v2026.11.24` |
