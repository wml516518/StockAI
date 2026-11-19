<template>
  <div id="app">
    <div v-if="authState === 'checking'" class="auth-checking" role="status">
      正在确认登录状态…
    </div>
    <LoginView
      v-else-if="authState === 'signed-out'"
      @authenticated="handleAuthenticated"
    />
    <template v-else>
      <Header :username="username" @logout="handleLogout" />
      <Tabs />
      <router-view v-slot="{ Component }">
        <keep-alive>
          <component :is="Component" />
        </keep-alive>
      </router-view>
    </template>
  </div>
</template>

<script setup>
import { onBeforeUnmount, onMounted, ref } from 'vue'
import Header from './components/Header.vue'
import LoginView from './components/LoginView.vue'
import Tabs from './components/Tabs.vue'
import { getSession, logout } from './services/authService'

const authState = ref('checking')
const username = ref('')

onMounted(async () => {
  window.addEventListener('stockai:unauthorized', handleUnauthorized)
  try {
    const session = await getSession()
    setSession(session)
  } catch {
    authState.value = 'signed-out'
  }
})

onBeforeUnmount(() => {
  window.removeEventListener('stockai:unauthorized', handleUnauthorized)
})

function setSession(session) {
  if (session?.authenticated) {
    username.value = session.username || ''
    authState.value = 'signed-in'
  } else {
    username.value = ''
    authState.value = 'signed-out'
  }
}

function handleAuthenticated(session) {
  setSession(session)
}

async function handleLogout() {
  try {
    await logout()
  } finally {
    handleUnauthorized()
  }
}

function handleUnauthorized() {
  username.value = ''
  authState.value = 'signed-out'
}
</script>

<style scoped>
#app {
  min-height: 100vh;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
}

.auth-checking {
  display: grid;
  min-height: 100vh;
  place-items: center;
  color: white;
  font-size: 15px;
}
</style>

