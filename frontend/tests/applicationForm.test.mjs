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

test('authorization_code validation requires a non-empty redirect URI', async () => {
  const { validateApplicationForm } = await loadFormModule()
  const form = baseForm()
  form.clientType = 'public'
  form.selectedGrantTypes = ['authorization_code']

  assert.equal(validateApplicationForm(form, false), 'authCodeRequiresRedirectUri')
  form.redirectUrisText = 'https://client.example/callback'
  assert.equal(validateApplicationForm(form, false), null)
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

test('edit payload omits empty display URLs so existing settings are preserved', async () => {
  const { buildApplicationPayload } = await loadFormModule()

  const payload = buildApplicationPayload(baseForm(), true)

  assert.equal(Object.hasOwn(payload, 'clientUrl'), false)
  assert.equal(Object.hasOwn(payload, 'clientLogoUrl'), false)
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

test('detail mapping treats a null client type as public', async () => {
  const { applicationFormFromDetail } = await loadFormModule()

  const form = applicationFormFromDetail({
    id: 'app-1',
    clientId: 'client-1',
    clientType: null
  })

  assert.equal(form.clientType, 'public')
})

test('detail loading returns no form when the request fails', async () => {
  const { loadApplicationForm } = await loadFormModule()

  const failed = await loadApplicationForm('app-1', async () => ({ code: 500 }))
  const missing = await loadApplicationForm('app-1', async () => ({ code: 200 }))
  assert.equal(failed, null)
  assert.equal(missing, null)
})

test('edit loads full detail using the id from an incomplete list row', async () => {
  const { createApplicationEditController } = await loadFormModule()
  let requestedId = ''
  let state
  const controller = createApplicationEditController(async id => {
    requestedId = id
    return { code: 200, data: detail(id) }
  }, nextState => { state = nextState })

  assert.equal(await controller.openEdit('app-only-id'), 'loaded')
  assert.equal(requestedId, 'app-only-id')
  assert.equal(state.editId, 'app-only-id')
  assert.equal(state.form.clientId, 'app-only-id')
})

function deferred() {
  let resolve
  const promise = new Promise(value => { resolve = value })
  return { promise, resolve }
}

function detail(id) {
  return {
    id,
    clientId: id,
    displayName: id,
    clientType: 'public',
    applicationType: 'web',
    consentType: 'implicit',
    enabled: 'true',
    requirePkce: true
  }
}

test('out-of-order edit responses keep the latest application selected', async () => {
  const { createApplicationEditController } = await loadFormModule()
  const requests = new Map()
  let state
  const controller = createApplicationEditController(async id => {
    const request = deferred()
    requests.set(id, request)
    return request.promise
  }, nextState => { state = nextState })

  const first = controller.openEdit('app-a')
  const second = controller.openEdit('app-b')

  requests.get('app-b').resolve({ code: 200, data: detail('app-b') })
  assert.equal(await second, 'loaded')
  assert.equal(state.editId, 'app-b')
  assert.equal(state.form.clientId, 'app-b')

  requests.get('app-a').resolve({ code: 200, data: detail('app-a') })
  assert.equal(await first, 'stale')
  assert.equal(state.editId, 'app-b')
  assert.equal(state.form.clientId, 'app-b')
})

test('a pending edit cannot reopen the modal after the user starts adding an application', async () => {
  const { createApplicationEditController } = await loadFormModule()
  const request = deferred()
  let state
  const controller = createApplicationEditController(async () => request.promise, nextState => { state = nextState })

  const pending = controller.openEdit('app-a')
  controller.openAdd()
  assert.equal(state.showModal, true)
  assert.equal(state.editing, false)
  assert.equal(state.form.clientId, '')

  request.resolve({ code: 200, data: detail('app-a') })
  assert.equal(await pending, 'stale')
  assert.equal(state.showModal, true)
  assert.equal(state.editing, false)
  assert.equal(state.form.clientId, '')
})

test('closing an edit invalidates its pending detail request', async () => {
  const { createApplicationEditController } = await loadFormModule()
  const request = deferred()
  let state
  const controller = createApplicationEditController(async () => request.promise, nextState => { state = nextState })

  const pending = controller.openEdit('app-a')
  controller.close()
  request.resolve({ code: 200, data: detail('app-a') })

  assert.equal(await pending, 'stale')
  assert.equal(state.showModal, false)
  assert.equal(state.editId, '')
  assert.equal(state.form.clientId, '')
})

test('a failed detail request clears the previous form instead of reusing it', async () => {
  const { createApplicationEditController } = await loadFormModule()
  const secondRequest = deferred()
  let state
  const controller = createApplicationEditController(
    id => id === 'app-a'
      ? Promise.resolve({ code: 200, data: detail('app-a') })
      : secondRequest.promise,
    nextState => { state = nextState })

  assert.equal(await controller.openEdit('app-a'), 'loaded')
  assert.equal(state.form.clientId, 'app-a')

  const failed = controller.openEdit('app-b')
  secondRequest.resolve({ code: 500 })
  assert.equal(await failed, 'failed')
  assert.equal(state.showModal, false)
  assert.equal(state.editId, '')
  assert.equal(state.form.clientId, '')
})

test('an exception during detail loading clears the previous form instead of reusing it', async () => {
  const { createApplicationEditController } = await loadFormModule()
  let state
  const controller = createApplicationEditController(
    id => id === 'app-a'
      ? Promise.resolve({ code: 200, data: detail('app-a') })
      : Promise.reject(new Error('network failure')),
    nextState => { state = nextState })

  assert.equal(await controller.openEdit('app-a'), 'loaded')
  assert.equal(state.form.clientId, 'app-a')

  assert.equal(await controller.openEdit('app-b'), 'failed')
  assert.equal(state.showModal, false)
  assert.equal(state.editId, '')
  assert.equal(state.form.clientId, '')
})
