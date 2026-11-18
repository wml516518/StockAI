<template>
  <div class="container">
    <div class="content">
      <!-- AI提示词配置 -->
      <div class="card">
        <h3>AI提示词配置</h3>
        <div class="form-group">
          <button class="btn" @click="loadPrompts">🔄 刷新提示词列表</button>
          <button class="btn" @click="showCreatePromptForm">➕ 添加提示词</button>
        </div>
        <div v-if="loadingPrompts" class="loading">加载中...</div>
        <div v-else-if="prompts.length === 0" class="no-data">暂无提示词，请添加</div>
        <table v-else>
          <thead>
            <tr>
              <th>名称</th>
              <th>温度</th>
              <th>默认</th>
              <th>启用</th>
              <th>操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="prompt in prompts" :key="prompt.id">
              <td>{{ prompt.name }}</td>
              <td>{{ prompt.temperature }}</td>
              <td>{{ prompt.isDefault ? '✓' : '' }}</td>
              <td>{{ prompt.isActive ? '✓' : '' }}</td>
              <td>
                <button class="btn btn-small" @click="editPrompt(prompt)">编辑</button>
                <button class="btn btn-danger btn-small" @click="deletePrompt(prompt.id)">删除</button>
              </td>
            </tr>
          </tbody>
        </table>

        <!-- 添加/编辑表单 -->
        <div v-if="showPromptForm" class="prompt-form">
          <h4>{{ editingPrompt?.id === 0 ? '添加新提示词' : '编辑提示词' }}</h4>
          <input type="hidden" v-model="editingPrompt.id">
          <div class="form-group">
            <label>名称 *</label>
            <input type="text" v-model="editingPrompt.name" placeholder="例如：基本面分析">
          </div>
          <div class="form-group">
            <label>系统提示词 *</label>
            <textarea v-model="editingPrompt.systemPrompt" rows="6" placeholder="输入系统提示词"></textarea>
          </div>
          <div class="form-group">
            <label>温度（0-2）</label>
            <input type="number" v-model.number="editingPrompt.temperature" step="0.1" min="0" max="2">
          </div>
          <div class="form-group">
            <label>
              <input type="checkbox" v-model="editingPrompt.isDefault">
              设为默认提示词
            </label>
          </div>
          <div class="form-group">
            <label>
              <input type="checkbox" v-model="editingPrompt.isActive">
              启用
            </label>
          </div>
          <div class="form-actions">
            <button class="btn" @click="savePrompt">💾 保存提示词</button>
            <button class="btn btn-secondary" @click="cancelPromptEdit">取消</button>
          </div>
        </div>
      </div>

      <!-- 股票行情自动刷新设置 -->
      <div class="card">
        <h3>股票行情自动刷新设置</h3>
        <div class="form-group">
          <label>刷新间隔（秒）</label>
          <input 
            v-model.number="refreshInterval" 
            type="number" 
            min="0.5" 
            max="60" 
            step="0.5"
          >
          <p class="help-text">
            推荐设置：0.5-2秒（实时） | 3-5秒（常规） | 10-30秒（省流量）
          </p>
        </div>
        <div class="form-group">
          <label>
            <input 
              type="checkbox" 
              v-model="autoRefreshEnabled"
              style="width: auto; margin-right: 5px;"
            >
            启用自动刷新
          </label>
        </div>
        <button class="btn" @click="saveSettings">💾 保存设置</button>
      </div>

      <!-- 当前状态 -->
      <div class="card">
        <h3>当前状态</h3>
        <div class="status-cards">
          <div class="status-card">
            <div class="status-label">刷新间隔</div>
            <div class="status-value">{{ refreshInterval }}秒</div>
          </div>
          <div class="status-card">
            <div class="status-label">自动刷新状态</div>
            <div class="status-value" :style="{ color: autoRefreshEnabled ? '#4caf50' : '#f44336' }">
              {{ autoRefreshEnabled ? '已启用' : '已禁用' }}
            </div>
          </div>
          <div class="status-card">
            <div class="status-label">上次刷新</div>
            <div class="status-value">{{ lastRefreshTime }}</div>
          </div>
        </div>
      </div>

      <!-- AI模型配置 -->
      <div class="card">
        <h3>AI模型配置</h3>
        <div class="form-group">
          <button class="btn" @click="loadConfigs">🔄 刷新配置列表</button>
          <button class="btn" @click="showCreateConfigForm">➕ 添加配置</button>
        </div>
        
        <!-- 配置列表 -->
        <div v-if="loadingConfigs" class="loading">加载中...</div>
        <div v-else-if="configs.length === 0" class="no-data">暂无AI模型配置，请添加配置</div>
        <table v-else>
          <thead>
            <tr>
              <th>名称</th>
              <th>模型名称</th>
              <th>订阅端点</th>
              <th>密钥</th>
              <th>状态</th>
              <th>默认</th>
              <th>操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="config in configs" :key="config.id">
              <td>{{ config.name }}</td>
              <td>{{ config.modelName || '-' }}</td>
              <td>{{ config.subscribeEndpoint }}</td>
              <td>
                <span :class="config.hasApiKey ? 'status-active' : 'status-inactive'">
                  {{ config.hasApiKey ? '已保存' : '未配置' }}
                </span>
              </td>
              <td>
                <span :class="config.isActive ? 'status-active' : 'status-inactive'">
                  {{ config.isActive ? '激活' : '未激活' }}
                </span>
              </td>
              <td>{{ config.isDefault ? '✓' : '' }}</td>
              <td>
                <button class="btn btn-small" @click="editConfig(config)">编辑</button>
                <button class="btn btn-danger btn-small" @click="deleteConfig(config.id)">删除</button>
                <button class="btn btn-small" @click="testConfig(config)">测试</button>
              </td>
            </tr>
          </tbody>
        </table>

        <!-- 添加/编辑表单 -->
        <div v-if="showConfigForm" class="config-form">
          <h4>{{ editingConfig?.id === 0 ? '添加新配置' : '编辑配置' }}</h4>
          <input type="hidden" v-model="editingConfig.id">
          <div class="form-group">
            <label>配置名称 *</label>
            <input type="text" v-model="editingConfig.name" placeholder="例如：通义千问API">
          </div>
          <div class="form-group">
            <label>API Key {{ editingConfig.id === 0 ? '*' : '' }}</label>
            <input
              type="password"
              v-model="editingConfig.apiKey"
              :placeholder="editingConfig.id === 0 ? '请输入API密钥' : '已保存，留空则不修改'"
              autocomplete="new-password"
            >
            <p v-if="editingConfig.id !== 0" class="help-text">
              密钥不会回传到浏览器。留空保存会继续使用服务端已保存的密钥。
            </p>
          </div>
          <div class="form-group">
            <label>订阅端点 *</label>
            <input 
              type="text" 
              v-model="editingConfig.subscribeEndpoint" 
              placeholder="例如：https://dashscope.aliyuncs.com/api/v1/services/aigc/text-generation/generation"
            >
          </div>
          <div class="form-group">
            <label>模型名称 *</label>
            <input type="text" v-model="editingConfig.modelName" placeholder="例如：qwen-max">
          </div>
          <div class="form-group">
            <label>
              <input type="checkbox" v-model="editingConfig.isActive">
              设为激活状态
            </label>
            <p class="help-text">
              激活状态下，系统将使用此配置进行AI分析
            </p>
          </div>
          <div class="form-group">
            <label>
              <input type="checkbox" v-model="editingConfig.isDefault">
              设为默认配置
            </label>
            <p class="help-text">
              默认配置将在创建新配置时自动选中
            </p>
          </div>
          <div class="form-actions">
            <button class="btn" @click="saveConfig">💾 保存配置</button>
            <button class="btn btn-secondary" @click="cancelConfigEdit">取消</button>
            <button class="btn btn-warning" @click="testConnection">🧪 测试连接</button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, onActivated } from 'vue'
import { useWatchlistStore } from '../stores/watchlist'
import { aiPromptService } from '../services/aiPromptService'
import { aiModelConfigService } from '../services/aiModelConfigService'

const watchlistStore = useWatchlistStore()

// 股票行情刷新设置
const refreshInterval = ref(3)
const autoRefreshEnabled = ref(true)
const lastRefreshTime = ref('--')

// AI提示词管理
const prompts = ref([])
const loadingPrompts = ref(false)
const showPromptForm = ref(false)
const editingPrompt = ref({
  id: 0,
  name: '',
  systemPrompt: '',
  temperature: 0.7,
  isDefault: false,
  isActive: true
})

// AI模型配置管理
const configs = ref([])
const loadingConfigs = ref(false)
const showConfigForm = ref(false)
const editingConfig = ref({
  id: 0,
  name: '',
  apiKey: '',
  hasApiKey: false,
  subscribeEndpoint: '',
  modelName: '',
  isActive: false,
  isDefault: false
})

onMounted(() => {
  loadSettings()
  loadPrompts()
  loadConfigs()
  updateLastRefreshTime()
})

onActivated(() => {
  loadSettings()
  loadPrompts()
  loadConfigs()
})

// 加载股票行情刷新设置
const loadSettings = () => {
  const savedInterval = localStorage.getItem('refreshInterval')
  const savedEnabled = localStorage.getItem('autoRefreshEnabled')
  
  if (savedInterval) {
    refreshInterval.value = parseFloat(savedInterval)
    watchlistStore.refreshInterval = refreshInterval.value
  } else {
    refreshInterval.value = watchlistStore.refreshInterval
  }
  
  if (savedEnabled !== null) {
    autoRefreshEnabled.value = savedEnabled === 'true'
    watchlistStore.autoRefreshEnabled = autoRefreshEnabled.value
  } else {
    autoRefreshEnabled.value = watchlistStore.autoRefreshEnabled
  }
}

// 保存股票行情刷新设置
const saveSettings = () => {
  watchlistStore.refreshInterval = refreshInterval.value
  watchlistStore.autoRefreshEnabled = autoRefreshEnabled.value
  localStorage.setItem('refreshInterval', refreshInterval.value.toString())
  localStorage.setItem('autoRefreshEnabled', autoRefreshEnabled.value.toString())
  updateLastRefreshTime()
  alert('设置已保存！刷新间隔将在下次刷新时生效。')
}

// 更新最后刷新时间
const updateLastRefreshTime = () => {
  const now = new Date()
  lastRefreshTime.value = now.toLocaleTimeString('zh-CN')
}

// AI提示词管理
const loadPrompts = async () => {
  loadingPrompts.value = true
  try {
    const data = await aiPromptService.getAll()
    prompts.value = Array.isArray(data) ? data : []
  } catch (error) {
    console.error('加载提示词失败：', error)
    prompts.value = []
    // 如果API返回404或空数据，不显示错误，只显示空列表
    const status = error?.response?.status || error?.status
    if (status !== 404 && status !== 200) {
      const errorMsg = error?.response?.data?.message || error?.message || error?.toString() || '未知错误'
      console.error('详细错误信息：', errorMsg)
    }
  } finally {
    loadingPrompts.value = false
  }
}

const showCreatePromptForm = () => {
  editingPrompt.value = {
    id: 0,
    name: '',
    systemPrompt: '',
    temperature: 0.7,
    isDefault: false,
    isActive: true
  }
  showPromptForm.value = true
}

const editPrompt = (prompt) => {
  editingPrompt.value = { ...prompt }
  showPromptForm.value = true
}

const savePrompt = async () => {
  if (!editingPrompt.value.name || !editingPrompt.value.systemPrompt) {
    alert('请填写名称和系统提示词')
    return
  }

  try {
    if (editingPrompt.value.id === 0) {
      await aiPromptService.create(editingPrompt.value)
    } else {
      await aiPromptService.update(editingPrompt.value.id, editingPrompt.value)
    }
    await loadPrompts()
    showPromptForm.value = false
    alert('保存成功！')
  } catch (error) {
    alert('保存失败：' + error.message)
  }
}

const deletePrompt = async (id) => {
  if (!confirm('确定要删除这个提示词吗？')) {
    return
  }

  try {
    await aiPromptService.delete(id)
    await loadPrompts()
    alert('删除成功！')
  } catch (error) {
    alert('删除失败：' + error.message)
  }
}

const cancelPromptEdit = () => {
  showPromptForm.value = false
}

// AI模型配置管理
const loadConfigs = async () => {
  loadingConfigs.value = true
  try {
    const data = await aiModelConfigService.getAll()
    configs.value = Array.isArray(data) ? data : []
  } catch (error) {
    console.error('加载AI模型配置失败：', error)
    configs.value = []
    // 如果API返回404或空数据，不显示错误，只显示空列表
    const status = error?.response?.status || error?.status
    if (status !== 404 && status !== 200) {
      const errorMsg = error?.response?.data?.message || error?.message || error?.toString() || '未知错误'
      console.error('详细错误信息：', errorMsg)
    }
  } finally {
    loadingConfigs.value = false
  }
}

const showCreateConfigForm = () => {
  editingConfig.value = {
    id: 0,
    name: '',
    apiKey: '',
    hasApiKey: false,
    subscribeEndpoint: '',
    modelName: '',
    isActive: false,
    isDefault: false
  }
  showConfigForm.value = true
}

const editConfig = (config) => {
  editingConfig.value = { ...config, apiKey: '' }
  showConfigForm.value = true
}

const saveConfig = async () => {
  const isCreate = editingConfig.value.id === 0
  if (!editingConfig.value.name
    || !editingConfig.value.subscribeEndpoint
    || !editingConfig.value.modelName
    || (isCreate && !editingConfig.value.apiKey)
    || (!isCreate && !editingConfig.value.hasApiKey && !editingConfig.value.apiKey)) {
    alert('请填写必填字段')
    return
  }

  try {
    const payload = { ...editingConfig.value }
    const configId = payload.id
    delete payload.id
    delete payload.hasApiKey
    if (isCreate) {
      await aiModelConfigService.create(payload)
    } else {
      await aiModelConfigService.update(configId, payload)
    }
    await loadConfigs()
    showConfigForm.value = false
    alert('保存成功！')
  } catch (error) {
    alert('保存失败：' + error.message)
  }
}

const deleteConfig = async (id) => {
  if (!confirm('确定要删除这个配置吗？')) {
    return
  }

  try {
    await aiModelConfigService.delete(id)
    await loadConfigs()
    alert('删除成功！')
  } catch (error) {
    alert('删除失败：' + error.message)
  }
}

const testConfig = async (config) => {
  try {
    await aiModelConfigService.testConnection({
      id: config.id
    })
    alert('连接测试成功！')
  } catch (error) {
    alert('连接测试失败：' + error.message)
  }
}

const testConnection = async () => {
  const isCreate = editingConfig.value.id === 0
  if (!editingConfig.value.subscribeEndpoint
    || !editingConfig.value.modelName
    || (isCreate && !editingConfig.value.apiKey)
    || (!isCreate && !editingConfig.value.hasApiKey && !editingConfig.value.apiKey)) {
    alert('请填写API Key、订阅端点和模型名称')
    return
  }

  try {
    const request = {
      subscribeEndpoint: editingConfig.value.subscribeEndpoint,
      modelName: editingConfig.value.modelName
    }
    if (!isCreate) {
      request.id = editingConfig.value.id
    }
    if (editingConfig.value.apiKey) {
      request.apiKey = editingConfig.value.apiKey
    }
    await aiModelConfigService.testConnection(request)
    alert('连接测试成功！')
  } catch (error) {
    alert('连接测试失败：' + error.message)
  }
}

const cancelConfigEdit = () => {
  showConfigForm.value = false
}
</script>

<style scoped>
.content {
  padding: 30px;
}

.card {
  margin-bottom: 30px;
}

.form-group {
  margin-bottom: 15px;
}

.form-group label {
  display: block;
  margin-bottom: 5px;
  font-weight: 500;
}

.form-group input[type="text"],
.form-group input[type="number"],
.form-group input[type="password"],
.form-group textarea {
  width: 100%;
  padding: 8px;
  border: 1px solid #ddd;
  border-radius: 4px;
  font-size: 14px;
}

.form-group textarea {
  resize: vertical;
}

.help-text {
  font-size: 0.85em;
  color: #666;
  margin-top: 5px;
}

.form-actions {
  display: flex;
  gap: 10px;
  margin-top: 20px;
}

table {
  width: 100%;
  border-collapse: collapse;
  margin-top: 15px;
}

table th,
table td {
  padding: 10px;
  text-align: left;
  border-bottom: 1px solid #ddd;
}

table th {
  background-color: #f5f5f5;
  font-weight: 600;
}

.btn-small {
  padding: 4px 8px;
  font-size: 12px;
  margin-right: 5px;
}

.btn-danger {
  background-color: #f44336;
  color: white;
}

.btn-danger:hover {
  background-color: #d32f2f;
}

.btn-secondary {
  background-color: #757575;
  color: white;
}

.btn-secondary:hover {
  background-color: #616161;
}

.btn-warning {
  background-color: #ff9800;
  color: white;
}

.btn-warning:hover {
  background-color: #f57c00;
}

.loading {
  text-align: center;
  padding: 20px;
  color: #666;
}

.no-data {
  text-align: center;
  padding: 20px;
  color: #999;
}

.status-cards {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 15px;
  margin-top: 15px;
}

.status-card {
  background: #f5f5f5;
  padding: 15px;
  border-radius: 8px;
  text-align: center;
}

.status-label {
  font-size: 0.9em;
  color: #666;
  margin-bottom: 8px;
}

.status-value {
  font-size: 1.2em;
  font-weight: 600;
  color: #333;
}

.status-active {
  color: #4caf50;
  font-weight: 600;
}

.status-inactive {
  color: #999;
}

.prompt-form,
.config-form {
  margin-top: 20px;
  padding: 20px;
  background: #f9f9f9;
  border-radius: 8px;
  border: 1px solid #ddd;
}

.prompt-form h4,
.config-form h4 {
  margin-top: 0;
  margin-bottom: 20px;
}
</style>
