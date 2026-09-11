import assert from 'node:assert/strict'
import test from 'node:test'

async function loadFormModule() {
  const module = await import('../src/views/applicationForm.ts').catch(() => null)
  assert.ok(module, 'applicationForm module should exist')
  return module
}

function baseForm() {
  return {
    clientId: 'client-1',
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

test('create validation requires a client secret or JWKS for confidential clients', async () => {
  const { buildApplicationPayload, validateApplicationForm } = await loadFormModule()
  const form = baseForm()

  assert.equal(validateApplicationForm(form, false), 'confidentialSecretRequired')
  form.jsonWebKeySet = '{"keys":[]}'
  assert.equal(validateApplicationForm(form, false), null)
  const payload = buildApplicationPayload(form, false)
  assert.equal(payload.clientSecret, null)
  assert.equal(payload.jsonWebKeySet, '{"keys":[]}')
})

test('edit validation allows omitted secret and JWKS', async () => {
  const { validateApplicationForm } = await loadFormModule()

  assert.equal(validateApplicationForm(baseForm(), true), null)
})

test('edit payload omits unchanged credentials but sends explicitly entered values', async () => {
  const { buildApplicationPayload } = await loadFormModule()
  const form = baseForm()

  const unchanged = buildApplicationPayload(form, true)
  assert.equal(Object.hasOwn(unchanged, 'clientSecret'), false)
  assert.equal(Object.hasOwn(unchanged, 'jsonWebKeySet'), false)

  form.clientSecret = 'new-secret'
  form.jsonWebKeySet = '{"keys":[{"kid":"new"}]}'
  const changed = buildApplicationPayload(form, true)
  assert.equal(changed.clientSecret, 'new-secret')
  assert.equal(changed.jsonWebKeySet, '{"keys":[{"kid":"new"}]}')
})

test('detail mapping uses detail values even when the list row is incomplete', async () => {
  const { applicationFormFromDetail } = await loadFormModule()
  const form = applicationFormFromDetail({
    id: 'app-1',
    clientId: 'client-1',
    displayName: 'Demo',
    applicationType: 'native',
    clientType: 'confidential',
    consentType: 'explicit',
    redirectUris: ['https://client.example/callback'],
    postLogoutRedirectUris: ['https://client.example/logout'],
    grantTypes: ['authorization_code'],
    scopes: ['openid'],
    clientUrl: 'https://client.example',
    clientLogoUrl: 'https://client.example/logo.png',
    enabled: 'false',
    requirePkce: false,
    accessTokenLifetime: 3600,
    authorizationCodeLifetime: 300,
    refreshTokenLifetime: 1209600,
    identityTokenLifetime: 3600,
    deviceCodeLifetime: 300,
    userCodeLifetime: 300
  })

  assert.equal(form.applicationType, 'native')
  assert.equal(form.accessTokenLifetime, 3600)
  assert.equal(form.refreshTokenLifetime, 1209600)
  assert.equal(form.requirePkce, false)
  assert.deepEqual(form.selectedGrantTypes, ['authorization_code'])
  assert.deepEqual(form.selectedScopes, ['openid'])
  assert.equal(form.redirectUrisText, 'https://client.example/callback')
})

test('detail loading returns no form when the request fails', async () => {
  const { loadApplicationForm } = await loadFormModule()

  const failed = await loadApplicationForm('app-1', async () => ({ code: 500 }))
  const missing = await loadApplicationForm('app-1', async () => ({ code: 200 }))
  assert.equal(failed, null)
  assert.equal(missing, null)
})
