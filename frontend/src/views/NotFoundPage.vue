<template>
  <div class="card error-page">
    <div class="error-icon">&#9888;</div>
    <h1>{{ $t('errors.notFound') }}</h1>
    <p>{{ $t('errors.notFoundDesc') }}</p>
    <router-link to="/account/login" class="btn btn-primary" style="margin-top:1rem;display:inline-block;text-decoration:none">{{ $t('errors.returnToLogin') }}</router-link>
  </div>
</template>
