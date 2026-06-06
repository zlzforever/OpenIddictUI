# Frontend — AGENTS.md

**Generated:** 2026-06-02

## OVERVIEW
Vue 3 + TypeScript + Vite SPA for STS login, consent, logout, and grants management. All pages lazy-loaded.

## STRUCTURE
```
frontend/src/
├── views/               # Page components (lazy-loaded via router)
├── composables/         # Stateful logic (useSession, useFetch, useProviders)
├── components/          # Reusable UI (PasswordLogin, SmsLogin, ExternalButtons)
├── router/index.ts      # Routes + auth guard (beforeEach)
├── main.ts              # App bootstrap
├── App.vue              # Root component
└── style.css            # Global styles
```

## WHERE TO LOOK
| Task | Location |
|------|----------|
| Auth guard + routing | `router/index.ts` — `beforeEach` checks `/session` |
| Login page | `views/LoginPage.vue` — three login methods |
| Consent page | `views/ConsentPage.vue` — scope display + grant |
| Session check | `composables/useSession.ts` — `GET /session` |
| XSRF token + API calls | `composables/useFetch.ts` — auto-attach X-XSRF-TOKEN |
| External login buttons | `components/ExternalButtons.vue` |
| Password login form | `components/PasswordLogin.vue` |
| SMS login form | `components/SmsLogin.vue` |
| Welcome/grants page | `views/WelcomePage.vue`, `views/GrantsPage.vue` |

## CONVENTIONS
- **Vue 3 Composition API**: `<script setup>` SFCs, composables for stateful logic
- **Lazy loading**: all routes use dynamic `() => import(...)` except redirects
- **Antiforgery**: `useFetch.ts` auto-sets `X-XSRF-TOKEN` header; CSRF enforced on POST endpoints
- **URL case**: `beforeEach` redirects uppercase paths to lowercase
- **Auth guard**: unauthenticated users → redirect to `/account/login`; authenticated → `/welcome`
- **No state store**: no Pinia/Vuex — composables manage reactive state directly

## NOTES
- `consent` requires `consentId` from URL params; `ConsentPage` fetches consent data via `GET /api/consent/{id}`
- `POST /account/logout` requires CSRF token; after logout, redirects to `/logged-out`
- Hash fragments in URL are stripped (redirect to `/not-found`)
- Login return URL must start with `/connect/authorize?` (whitelist enforced server-side)
