<template>
  <div class="header">
    <h1>📈 股票分析系统</h1>
    <p>实时行情 · 技术分析 · 智能推荐</p>
  </div>
</template>

<script setup>
</script>

<style scoped>
.header {
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

@media (max-width: 768px) {
  .header {
    padding: 20px;
  }
  
  .header h1 {
    font-size: 2em;
  }
}
</style>

