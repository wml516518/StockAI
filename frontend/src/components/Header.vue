<template>
  <header class="header">
    <h1>📈 股票分析系统</h1>
    <p>实时行情 · 技术分析 · 智能推荐</p>
    <div class="account">
      <span v-if="username">{{ username }}</span>
      <button type="button" @click="$emit('logout')">退出登录</button>
    </div>
  </header>
</template>

<script setup>
defineProps({
  username: {
    type: String,
    default: ''
  }
})
defineEmits(['logout'])
</script>

<style scoped>
.header {
  position: relative;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  color: white;
  padding: 30px;
  text-align: center;
}

.header h1 {
  font-size: 2.5em;
  margin-bottom: 10px;
}

.header p {
  font-size: 1.1em;
  opacity: 0.9;
}

.account {
  position: absolute;
  top: 16px;
  right: 22px;
  display: flex;
  align-items: center;
  gap: 12px;
  font-size: 13px;
}

.account button {
  padding: 7px 11px;
  color: white;
  background: rgba(255, 255, 255, 0.14);
  border: 1px solid rgba(255, 255, 255, 0.42);
  border-radius: 6px;
  cursor: pointer;
  font: inherit;
}

.account button:hover {
  background: rgba(255, 255, 255, 0.24);
}

@media (max-width: 768px) {
  .header {
    padding: 20px;
  }
  
  .header h1 {
    font-size: 2em;
  }

  .account {
    position: static;
    justify-content: center;
    margin-top: 14px;
  }
}
</style>

