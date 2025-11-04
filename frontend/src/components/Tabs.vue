<template>
  <div class="tabs">
    <div 
      v-for="tab in tabs" 
      :key="tab.path"
      :class="['tab', { active: isActive(tab.path) }]"
      @click="navigate(tab.path)"
    >
      {{ tab.label }}
    </div>
  </div>
</template>

<script setup>
import { useRouter, useRoute } from 'vue-router'

const router = useRouter()
const route = useRoute()

const tabs = [
  { path: '/watchlist', label: '自选股' },
  { path: '/screen', label: '条件选股' },
  { path: '/quant', label: '📊 量化交易' },
  { path: '/news', label: '金融新闻' },
  { path: '/ai', label: 'AI分析' },
  { path: '/alert', label: '价格提醒' },
  { path: '/settings', label: '⚙️ 设置' }
]

const isActive = (path) => {
  return route.path === path
}

const navigate = (path) => {
  router.push(path)
}
</script>

<style scoped>
.tabs {
  display: flex;
  background: #f5f5f5;
  border-bottom: 2px solid #ddd;
}

.tab {
  flex: 1;
  padding: 15px 20px;
  cursor: pointer;
  text-align: center;
  transition: all 0.3s;
  border-right: 1px solid #ddd;
}

.tab:last-child {
  border-right: none;
}

.tab:hover {
  background: #e0e0e0;
}

.tab.active {
  background: white;
  color: #667eea;
  font-weight: bold;
  border-bottom: 3px solid #667eea;
}

@media (max-width: 768px) {
  .tabs {
    flex-direction: column;
  }
  
  .tab {
    border-right: none;
    border-bottom: 1px solid #ddd;
  }
}
</style>

