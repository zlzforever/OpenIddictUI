export interface ApplicationForm {
  clientId: string
  clientSecret: string
  jsonWebKeySet: string
  displayName: string
  applicationType: string
  clientType: string
  consentType: string
  redirectUrisText: string
  postLogoutRedirectUrisText: string
  clientUrl: string
  clientLogoUrl: string
  accessTokenLifetime: number | null
  authorizationCodeLifetime: number | null
  refreshTokenLifetime: number | null
  identityTokenLifetime: number | null
  deviceCodeLifetime: number | null
  userCodeLifetime: number | null
  enabled: boolean
  requirePkce: boolean
  selectedScopes: string[]
  selectedGrantTypes: string[]
}

export interface ApplicationDetail {
  id: string
  clientId: string
  displayName?: string
  applicationType?: string
  clientType?: string
  consentType?: string
  redirectUris?: string[]
  postLogoutRedirectUris?: string[]
  grantTypes?: string[]
  scopes?: string[]
  clientUrl?: string
  clientLogoUrl?: string
  enabled?: string
  requirePkce?: boolean
  accessTokenLifetime?: number | null
  authorizationCodeLifetime?: number | null
  refreshTokenLifetime?: number | null
  identityTokenLifetime?: number | null
  deviceCodeLifetime?: number | null
  userCodeLifetime?: number | null
}

interface ApplicationDetailResponse {
  code: number
  data?: ApplicationDetail
}

export type ApplicationDetailLoader = (id: string) => Promise<ApplicationDetailResponse>

export function createApplicationForm(): ApplicationForm {
  return {
    clientId: '',
    clientSecret: '',
    jsonWebKeySet: '',
    displayName: '',
    applicationType: 'web',
    clientType: 'confidential',
    consentType: 'implicit',
    redirectUrisText: '',
    postLogoutRedirectUrisText: '',
    clientUrl: '',
    clientLogoUrl: '',
    accessTokenLifetime: null,
    authorizationCodeLifetime: null,
    refreshTokenLifetime: null,
    identityTokenLifetime: null,
    deviceCodeLifetime: null,
    userCodeLifetime: null,
    enabled: true,
    requirePkce: true,
    selectedScopes: [],
    selectedGrantTypes: []
  }
}

export function applicationFormFromDetail(detail: ApplicationDetail): ApplicationForm {
  return {
    ...createApplicationForm(),
    clientId: detail.clientId,
    displayName: detail.displayName || '',
    applicationType: detail.applicationType || 'web',
    clientType: detail.clientType || 'confidential',
    consentType: detail.consentType || 'implicit',
    redirectUrisText: (detail.redirectUris || []).join('\n'),
    postLogoutRedirectUrisText: (detail.postLogoutRedirectUris || []).join('\n'),
    clientUrl: detail.clientUrl || '',
    clientLogoUrl: detail.clientLogoUrl || '',
    accessTokenLifetime: detail.accessTokenLifetime ?? null,
    authorizationCodeLifetime: detail.authorizationCodeLifetime ?? null,
    refreshTokenLifetime: detail.refreshTokenLifetime ?? null,
    identityTokenLifetime: detail.identityTokenLifetime ?? null,
    deviceCodeLifetime: detail.deviceCodeLifetime ?? null,
    userCodeLifetime: detail.userCodeLifetime ?? null,
    enabled: detail.enabled === 'true',
    requirePkce: detail.requirePkce ?? true,
    selectedScopes: [...(detail.scopes || [])],
    selectedGrantTypes: [...(detail.grantTypes || [])]
  }
}

export async function loadApplicationForm(
  id: string,
  loadDetail: ApplicationDetailLoader
): Promise<ApplicationForm | null> {
  try {
    const response = await loadDetail(id)
    if (response.code !== 200 || !response.data) {
      return null
    }
    return applicationFormFromDetail(response.data)
  } catch {
    return null
  }
}

export interface ApplicationEditState {
  form: ApplicationForm
  editing: boolean
  editId: string
  showModal: boolean
}

export type ApplicationEditResult = 'loaded' | 'failed' | 'stale'

type ApplicationEditStateListener = (state: ApplicationEditState) => void

export function createApplicationEditController(
  loadDetail: ApplicationDetailLoader,
  applyState: ApplicationEditStateListener
) {
  let requestVersion = 0

  const closedState = (): ApplicationEditState => ({
    form: createApplicationForm(),
    editing: false,
    editId: '',
    showModal: false
  })

  const invalidate = () => ++requestVersion

  return {
    openAdd() {
      invalidate()
      applyState({ ...closedState(), showModal: true })
    },

    close() {
      invalidate()
      applyState(closedState())
    },

    async openEdit(id: string): Promise<ApplicationEditResult> {
      const version = invalidate()
      applyState(closedState())

      const loadedForm = await loadApplicationForm(id, loadDetail)
      if (version !== requestVersion) {
        return 'stale'
      }
      if (!loadedForm) {
        applyState(closedState())
        return 'failed'
      }

      applyState({
        form: loadedForm,
        editing: true,
        editId: id,
        showModal: true
      })
      return 'loaded'
    }
  }
}

export type ApplicationValidationMessage =
  | 'clientIdRequired'
  | 'confidentialSecretRequired'
  | 'authCodeRequiresRedirectUri'

export function validateApplicationForm(
  form: ApplicationForm,
  editing: boolean
): ApplicationValidationMessage | null {
  if (!form.clientId.trim()) return 'clientIdRequired'
  if (!editing && form.clientType === 'confidential' &&
      !form.clientSecret.trim() && !form.jsonWebKeySet.trim()) {
    return 'confidentialSecretRequired'
  }
  if (form.selectedGrantTypes.includes('authorization_code') && !form.redirectUrisText.trim()) {
    return 'authCodeRequiresRedirectUri'
  }
  return null
}

export function buildApplicationPayload(
  form: ApplicationForm,
  editing: boolean
): Record<string, unknown> {
  const body: Record<string, unknown> = {
    clientId: form.clientId,
    displayName: form.displayName,
    applicationType: form.applicationType,
    clientType: form.clientType,
    consentType: form.consentType,
    redirectUris: form.redirectUrisText.split('\n').filter(value => value.trim()),
    postLogoutRedirectUris: form.postLogoutRedirectUrisText.split('\n').filter(value => value.trim()),
    clientUrl: form.clientUrl || null,
    clientLogoUrl: form.clientLogoUrl || null,
    accessTokenLifetime: form.accessTokenLifetime,
    authorizationCodeLifetime: form.authorizationCodeLifetime,
    refreshTokenLifetime: form.refreshTokenLifetime,
    identityTokenLifetime: form.identityTokenLifetime,
    deviceCodeLifetime: form.deviceCodeLifetime,
    userCodeLifetime: form.userCodeLifetime,
    scopes: form.selectedScopes,
    grantTypes: form.selectedGrantTypes,
    enabled: form.enabled,
    requirePkce: form.requirePkce
  }

  const clientSecret = form.clientSecret.trim()
  const jsonWebKeySet = form.jsonWebKeySet.trim()
  if (editing) {
    if (clientSecret) body.clientSecret = form.clientSecret
    if (jsonWebKeySet) body.jsonWebKeySet = form.jsonWebKeySet
  } else {
    body.clientSecret = clientSecret || null
    body.jsonWebKeySet = jsonWebKeySet || null
  }

  return body
}
