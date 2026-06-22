# Application Management — Full Configuration Support

**Date:** 2026-06-08
**Status:** Design Approved

## Goal

Expand the Application management page to cover all 14 properties of `OpenIddictApplicationDescriptor`, organized into clear tabs.

## Current State

8 of 14 properties are exposed. Missing: `ApplicationType`, `DisplayNames`, `JsonWebKeySet`, `Properties`, and granular `Requirements`. Current 5 tabs mix concerns (e.g., Enable/RequirePkce in Basic).

## New Tab Structure (6 tabs)

| Tab | Fields | Descriptor Property |
|-----|--------|---------------------|
| **Basic** | ClientId, DisplayName, ApplicationType, ClientType, ConsentType | `ClientId`, `DisplayName`, `ApplicationType`, `ClientType`, `ConsentType` |
| **Security** | ClientSecret, RequirePkce, JsonWebKeySet | `ClientSecret`, `Requirements`, `JsonWebKeySet` |
| **Grants & Scopes** | GrantTypes checkboxes, Scopes checkboxes | `Permissions` (`gt:*`, `scp:*`) |
| **URIs** | RedirectUris, PostLogoutRedirectUris | `RedirectUris`, `PostLogoutRedirectUris` |
| **Requirements** | Feature switches (PKCE, etc.) | `Requirements` |
| **Display** | ClientUrl, ClientLogoUrl, DisplayNames, Enabled | `Settings`, `DisplayNames` |

## Backend Changes

### ApplicationInput (new fields)

```csharp
string? ApplicationType     // "web" | "native" | "machine"
string? JsonWebKeySet       // raw JWKS JSON (textarea)
Dictionary<string,string>? DisplayNames  // localization
```

### BuildDescriptor (new mappings)

- `ApplicationType` → `d.ApplicationType`
- `JsonWebKeySet` → deserialize to `OpenIddict.Abstractions.JsonWebKeySet`, write to `d.JsonWebKeySet`
- `DisplayNames` → iterate, write to `d.DisplayNames`
- Requirements already handled by `d.Requirements.Add()`

### List endpoint

Return `applicationType`, `displayNames` in the response JSON for pre-filling edit forms.

## Frontend Changes

**`ApplicationPage.vue`** — Full rewrite of the modal form with `<n-tabs>` containing 6 `<n-tab-pane>` sections.

**New form fields:**

| Field | Component | Notes |
|-------|-----------|-------|
| ApplicationType | `<n-select>` | options: web, native, machine |
| JsonWebKeySet | `<n-input type="textarea">` | raw JSON, only for confidential clients |
| DisplayNames | dynamic key-value editor | `[+ 添加语言]` button, renders input pairs |
| Requirements | `<n-checkbox-group>` | checkboxes for each feature flag |

**`ApplicationPage.vue` table columns** — add ApplicationType column.

## Requirements Constants (OpenIddict)

Available feature flags from `OpenIddictConstants.Requirements.Features`:
- `ProofKeyForCodeExchange` — PKCE (already supported)
- `TokenRevocation` — token revocation endpoint

Only expose flags the STS supports. New flags can be added to the checkbox list without code changes.

## Security

`ClientSecret` field is disabled when `ClientType === "public"` (existing behavior). `JsonWebKeySet` field should be conditionally shown for confidential clients.

## Scope

**In scope:** ApplicationType, DisplayNames, JsonWebKeySet, Requirements reorganization, 6-tab layout.

**Out of scope:** `Properties` dictionary (rarely used, can add later), `Permissions` detail editor (auto-built from Grants/Scopes/URIs selections).
