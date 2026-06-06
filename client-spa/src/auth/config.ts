import { UserManager, WebStorageStateStore } from 'oidc-client-ts'

const authority = 'http://localhost:5164'

export const userManager = new UserManager({
  authority,
  client_id: 'spa-client',
  redirect_uri: `${window.location.origin}/signin-redirect-callback`,
  silent_redirect_uri: `${window.location.origin}/signin-silent-callback`,
  post_logout_redirect_uri: window.location.origin,
  response_type: 'code',
  scope: 'openid profile api1',
  loadUserInfo: false,
  userStore: new WebStorageStateStore({ store: window.localStorage }),
})
