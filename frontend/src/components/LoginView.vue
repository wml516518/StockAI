<template>
  <main class="login-page">
    <section class="login-card" aria-labelledby="login-title">
      <div class="brand-mark" aria-hidden="true">📈</div>
      <p class="brand-name">StockAI</p>
      <h1 id="login-title">登录研究平台</h1>
      <p class="login-intro">输入账号密码后查看自选股、筛选和回测结果。</p>

      <form @submit.prevent="submit">
        <label for="username">用户名</label>
        <input
          id="username"
          ref="usernameInput"
          v-model.trim="username"
          name="username"
          type="text"
          autocomplete="username"
          autocapitalize="none"
          spellcheck="false"
          required
          :disabled="submitting"
        />

        <label for="password">密码</label>
        <input
          id="password"
          v-model="password"
          name="password"
          type="password"
          autocomplete="current-password"
          required
          :disabled="submitting"
        />

        <p v-if="errorMessage" class="login-error" role="alert">{{ errorMessage }}</p>
        <button type="submit" :disabled="submitting">
          {{ submitting ? '正在登录…' : '登录' }}
        </button>
      </form>

      <a class="back-link" href="/">返回个人主页</a>
    </section>
  </main>
</template>

<script setup>
import { nextTick, onMounted, ref } from 'vue'
import { login } from '../services/authService'

const emit = defineEmits(['authenticated'])
const usernameInput = ref(null)
const username = ref('')
const password = ref('')
const submitting = ref(false)
const errorMessage = ref('')

onMounted(() => {
  nextTick(() => usernameInput.value?.focus())
})

async function submit() {
  if (submitting.value) return
  submitting.value = true
  errorMessage.value = ''

  try {
    const session = await login(username.value, password.value)
    password.value = ''
    emit('authenticated', session)
  } catch (error) {
    if (error.response?.status === 401) {
      errorMessage.value = '用户名或密码不正确'
    } else if (error.response?.status === 429) {
      errorMessage.value = '尝试次数过多，请稍后再试'
    } else {
      errorMessage.value = '暂时无法登录，请稍后重试'
    }
  } finally {
    submitting.value = false
  }
}
</script>

<style scoped>
.login-page {
  min-height: 100vh;
  display: grid;
  place-items: center;
  padding: 24px;
  background:
    radial-gradient(circle at 15% 15%, rgba(255, 255, 255, 0.2), transparent 28%),
    linear-gradient(135deg, #667eea 0%, #764ba2 100%);
}

.login-card {
  width: min(100%, 420px);
  padding: 42px;
  color: #273044;
  background: rgba(255, 255, 255, 0.97);
  border: 1px solid rgba(255, 255, 255, 0.7);
  border-radius: 22px;
  box-shadow: 0 28px 80px rgba(35, 25, 74, 0.3);
}

.brand-mark {
  font-size: 36px;
  line-height: 1;
}

.brand-name {
  margin: 12px 0 28px;
  color: #667eea;
  font-size: 14px;
  font-weight: 800;
  letter-spacing: 0.14em;
  text-transform: uppercase;
}

h1 {
  margin-bottom: 10px;
  font-size: 30px;
}

.login-intro {
  margin-bottom: 28px;
  color: #65708a;
  line-height: 1.7;
}

form {
  display: grid;
  gap: 10px;
}

label {
  margin-top: 8px;
  font-size: 14px;
  font-weight: 700;
}

input {
  width: 100%;
  padding: 13px 14px;
  color: #273044;
  background: #f8f9fd;
  border: 1px solid #d8ddeb;
  border-radius: 9px;
  font: inherit;
  outline: none;
}

input:focus {
  background: white;
  border-color: #667eea;
  box-shadow: 0 0 0 3px rgba(102, 126, 234, 0.14);
}

button {
  margin-top: 16px;
  padding: 13px 18px;
  color: white;
  background: #667eea;
  border: 0;
  border-radius: 9px;
  cursor: pointer;
  font: inherit;
  font-weight: 700;
}

button:hover:not(:disabled) {
  background: #5367d5;
}

button:disabled,
input:disabled {
  cursor: wait;
  opacity: 0.7;
}

.login-error {
  margin-top: 8px;
  color: #c8324b;
  font-size: 14px;
}

.back-link {
  display: inline-block;
  margin-top: 26px;
  color: #65708a;
  font-size: 14px;
  text-decoration: none;
}

.back-link:hover {
  color: #5367d5;
  text-decoration: underline;
}

@media (max-width: 520px) {
  .login-page {
    padding: 14px;
  }

  .login-card {
    padding: 28px 22px;
  }
}
</style>
