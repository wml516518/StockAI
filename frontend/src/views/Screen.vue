<template>
  <div class="container">
    <div class="content">
      <!-- 选股条件模板 -->
      <div class="card">
        <h3>选股条件模板</h3>
        <div class="template-controls">
          <select v-model="selectedTemplateId" style="flex: 1;">
            <option value="">选择模板...</option>
            <option v-for="template in templates" :key="template.id" :value="template.id">
              {{ template.name }}<span v-if="template.isDefault"> (默认)</span>
            </option>
          </select>
          <button class="btn btn-success" @click="loadTemplate" :disabled="!selectedTemplateId">📂 加载</button>
          <button class="btn btn-info" @click="showSaveDialog = true">💾 保存</button>
          <button class="btn btn-warning" @click="editTemplate" :disabled="!selectedTemplateId">✏️ 编辑</button>
          <button class="btn btn-danger" @click="deleteTemplate" :disabled="!selectedTemplateId">🗑️ 删除</button>
        </div>
      </div>

      <!-- 保存模板对话框 -->
      <div v-if="showSaveDialog" class="modal" @click.self="showSaveDialog = false">
        <div class="modal-content">
          <div class="modal-header">
            <h3>{{ editingTemplateId ? '编辑模板' : '保存选股模板' }}</h3>
            <span class="close" @click="showSaveDialog = false">&times;</span>
          </div>
          <div class="modal-body">
            <div class="form-group">
              <label>模板名称 *</label>
              <input v-model="templateForm.name" type="text" placeholder="输入模板名称" required>
            </div>
            <div class="form-group">
              <label>模板描述</label>
              <textarea v-model="templateForm.description" placeholder="输入模板描述（可选）" rows="3"></textarea>
            </div>
            <div class="form-group">
              <label>
                <input type="checkbox" v-model="templateForm.isDefault"> 设为默认模板
              </label>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn" @click="saveTemplate">💾 保存</button>
            <button class="btn btn-secondary" @click="showSaveDialog = false">取消</button>
          </div>
        </div>
      </div>

      <!-- 设置选股条件 -->
      <div class="card">
        <h3>设置选股条件</h3>
        <div class="form-grid">
          <div class="form-group">
            <label>市场</label>
            <select v-model="criteria.market">
              <option value="">全部市场</option>
              <option value="SH">上海市场</option>
              <option value="SZ">深圳市场</option>
            </select>
          </div>
          <div class="form-group">
            <label>价格区间（元）</label>
            <div style="display: flex; gap: 10px;">
              <input v-model.number="criteria.minPrice" type="number" step="0.01" placeholder="最低价">
              <input v-model.number="criteria.maxPrice" type="number" step="0.01" placeholder="最高价">
            </div>
          </div>
          <div class="form-group">
            <label>涨跌幅（%）</label>
            <div style="display: flex; gap: 10px;">
              <input v-model.number="criteria.minChangePercent" type="number" step="0.01" placeholder="最低涨幅">
              <input v-model.number="criteria.maxChangePercent" type="number" step="0.01" placeholder="最高涨幅">
            </div>
          </div>
          <div class="form-group">
            <label>换手率（%）</label>
            <div style="display: flex; gap: 10px;">
              <input v-model.number="criteria.minTurnoverRate" type="number" step="0.01" placeholder="最低换手率">
              <input v-model.number="criteria.maxTurnoverRate" type="number" step="0.01" placeholder="最高换手率">
            </div>
          </div>
          <div class="form-group">
            <label>成交量（手）</label>
            <div style="display: flex; gap: 10px;">
              <input v-model.number="criteria.minVolume" type="number" placeholder="最低成交量">
              <input v-model.number="criteria.maxVolume" type="number" placeholder="最高成交量">
            </div>
          </div>
          <div class="form-group">
            <label>市值区间（万元）</label>
            <div style="display: flex; gap: 10px;">
              <input v-model.number="criteria.minMarketValue" type="number" placeholder="最低市值">
              <input v-model.number="criteria.maxMarketValue" type="number" placeholder="最高市值">
            </div>
          </div>
          <div class="form-group">
            <label>市盈率(PE)</label>
            <div style="display: flex; gap: 10px;">
              <input v-model.number="criteria.minPE" type="number" step="0.01" placeholder="最低PE">
              <input v-model.number="criteria.maxPE" type="number" step="0.01" placeholder="最高PE">
            </div>
          </div>
          <div class="form-group">
            <label>市净率(PB)</label>
            <div style="display: flex; gap: 10px;">
              <input v-model.number="criteria.minPB" type="number" step="0.01" placeholder="最低PB">
              <input v-model.number="criteria.maxPB" type="number" step="0.01" placeholder="最高PB">
            </div>
          </div>
          <div class="form-group">
            <label>股息率（%）</label>
            <div style="display: flex; gap: 10px;">
              <input v-model.number="criteria.minDividendYield" type="number" step="0.01" placeholder="最低股息率">
              <input v-model.number="criteria.maxDividendYield" type="number" step="0.01" placeholder="最高股息率">
            </div>
          </div>
        </div>
        <div class="form-actions">
          <button class="btn" @click="handleScreen" :disabled="loading">🔍 开始选股</button>
          <button class="btn btn-secondary" @click="clearConditions">🧹 清空条件</button>
        </div>
      </div>

      <div class="card">
        <h3>选股结果</h3>
        <div v-if="loading" class="loading">
          <div>🔍 正在查询中，请稍候...</div>
          <div style="font-size: 0.9em; color: #666; margin-top: 10px;">
            数据量大时可能需要较长时间，请耐心等待
          </div>
        </div>
        <div v-else-if="results.length === 0" class="loading">等待查询...</div>
        <div v-else class="results-table">
          <table>
            <thead>
              <tr>
                <th>股票代码</th>
                <th>股票名称</th>
                <th>当前价</th>
                <th>涨跌幅</th>
                <th>市盈率</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="stock in results" :key="stock.code">
                <td>{{ stock.code }}</td>
                <td>{{ stock.name || '-' }}</td>
                <td>{{ formatPrice(stock.price) }}</td>
                <td :class="getPriceClass(stock.changePercent)">
                  {{ formatPercent(stock.changePercent) }}
                </td>
                <td>{{ stock.pe || '-' }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, onActivated } from 'vue'
import api from '../services/api'
import { screenTemplateService } from '../services/screenTemplateService'

const loading = ref(false)
const results = ref([])
const templates = ref([])
const selectedTemplateId = ref('')
const showSaveDialog = ref(false)
const editingTemplateId = ref(null)

const criteria = ref({
  market: '',
  minPrice: null,
  maxPrice: null,
  minChangePercent: null,
  maxChangePercent: null,
  minTurnoverRate: null,
  maxTurnoverRate: null,
  minVolume: null,
  maxVolume: null,
  minMarketValue: null,
  maxMarketValue: null,
  minPE: null,
  maxPE: null,
  minPB: null,
  maxPB: null,
  minDividendYield: null,
  maxDividendYield: null
})

const templateForm = ref({
  name: '',
  description: '',
  isDefault: false
})

onMounted(async () => {
  await loadTemplates()
})

onActivated(async () => {
  await loadTemplates()
})

const loadTemplates = async () => {
  try {
    templates.value = await screenTemplateService.getAll()
    // 如果有默认模板，自动选中
    const defaultTemplate = templates.value.find(t => t.isDefault)
    if (defaultTemplate) {
      selectedTemplateId.value = defaultTemplate.id
    }
  } catch (error) {
    console.error('加载模板失败:', error)
  }
}

const loadTemplate = async () => {
  if (!selectedTemplateId.value) return
  try {
    const templateCriteria = await screenTemplateService.toCriteria(selectedTemplateId.value)
    // 将模板条件应用到当前表单
    criteria.value = {
      market: templateCriteria.market || '',
      minPrice: templateCriteria.minPrice,
      maxPrice: templateCriteria.maxPrice,
      minChangePercent: templateCriteria.minChangePercent,
      maxChangePercent: templateCriteria.maxChangePercent,
      minTurnoverRate: templateCriteria.minTurnoverRate,
      maxTurnoverRate: templateCriteria.maxTurnoverRate,
      minVolume: templateCriteria.minVolume,
      maxVolume: templateCriteria.maxVolume,
      minMarketValue: templateCriteria.minMarketValue,
      maxMarketValue: templateCriteria.maxMarketValue,
      minPE: templateCriteria.minPE,
      maxPE: templateCriteria.maxPE,
      minPB: templateCriteria.minPB,
      maxPB: templateCriteria.maxPB,
      minDividendYield: templateCriteria.minDividendYield,
      maxDividendYield: templateCriteria.maxDividendYield
    }
  } catch (error) {
    console.error('加载模板失败:', error)
    alert('加载模板失败: ' + (error.response?.data?.message || error.message))
  }
}

const saveTemplate = async () => {
  if (!templateForm.value.name) {
    alert('请输入模板名称')
    return
  }
  try {
    const templateData = {
      ...templateForm.value,
      ...criteria.value
    }
    
    if (editingTemplateId.value) {
      templateData.id = editingTemplateId.value
      await screenTemplateService.update(editingTemplateId.value, templateData)
      alert('模板更新成功')
    } else {
      await screenTemplateService.create(templateData)
      alert('模板保存成功')
    }
    
    showSaveDialog.value = false
    templateForm.value = { name: '', description: '', isDefault: false }
    editingTemplateId.value = null
    await loadTemplates()
  } catch (error) {
    console.error('保存模板失败:', error)
    alert('保存模板失败: ' + (error.response?.data?.message || error.message))
  }
}

const editTemplate = async () => {
  if (!selectedTemplateId.value) return
  try {
    const template = await screenTemplateService.getById(selectedTemplateId.value)
    templateForm.value = {
      name: template.name,
      description: template.description || '',
      isDefault: template.isDefault
    }
    editingTemplateId.value = template.id
    showSaveDialog.value = true
  } catch (error) {
    console.error('加载模板失败:', error)
    alert('加载模板失败: ' + (error.response?.data?.message || error.message))
  }
}

const deleteTemplate = async () => {
  if (!selectedTemplateId.value) return
  if (!confirm('确定要删除这个模板吗？')) return
  try {
    await screenTemplateService.delete(selectedTemplateId.value)
    alert('模板删除成功')
    selectedTemplateId.value = ''
    await loadTemplates()
  } catch (error) {
    console.error('删除模板失败:', error)
    alert('删除模板失败: ' + (error.response?.data?.message || error.message))
  }
}

const handleScreen = async () => {
  loading.value = true
  try {
    // 使用 search 端点，返回分页结果
    const criteriaWithPagination = {
      ...criteria.value,
      pageIndex: 1,
      pageSize: 100 // 获取前100条结果
    }
    // 选股操作可能需要较长时间，设置更长的超时时间（5分钟）
    const response = await api.post('/screen/search', criteriaWithPagination, {
      timeout: 300000 // 5分钟 = 300000毫秒
    })
    // 处理分页响应
    results.value = response?.items || response || []
  } catch (error) {
    console.error('选股失败:', error)
    if (error.code === 'ECONNABORTED' || error.message?.includes('timeout')) {
      alert('选股超时：查询时间过长，请尝试缩小筛选条件范围或减少查询数量。')
    } else {
      alert('选股失败: ' + (error.response?.data?.message || error.message))
    }
  } finally {
    loading.value = false
  }
}

const clearConditions = () => {
  criteria.value = {
    market: '',
    minPrice: null,
    maxPrice: null,
    minChangePercent: null,
    maxChangePercent: null,
    minTurnoverRate: null,
    maxTurnoverRate: null,
    minVolume: null,
    maxVolume: null,
    minMarketValue: null,
    maxMarketValue: null,
    minPE: null,
    maxPE: null,
    minPB: null,
    maxPB: null,
    minDividendYield: null,
    maxDividendYield: null
  }
  results.value = []
}

const formatPrice = (price) => {
  if (price === null || price === undefined) return '-'
  return price.toFixed(2)
}

const formatPercent = (percent) => {
  if (percent === null || percent === undefined) return '-'
  return (percent > 0 ? '+' : '') + percent.toFixed(2) + '%'
}

const getPriceClass = (value) => {
  if (!value) return ''
  return value > 0 ? 'price-up' : value < 0 ? 'price-down' : ''
}
</script>

<style scoped>
.content {
  padding: 30px;
}

.template-controls {
  display: flex;
  gap: 10px;
  margin-bottom: 15px;
  align-items: center;
  flex-wrap: wrap;
}

.template-controls select {
  flex: 1;
  min-width: 200px;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
  gap: 15px;
  margin-bottom: 20px;
}

.form-actions {
  display: flex;
  gap: 10px;
}

.results-table {
  margin-top: 20px;
  overflow-x: auto;
}

table {
  width: 100%;
  border-collapse: collapse;
}

table th,
table td {
  padding: 12px;
  text-align: left;
  border-bottom: 1px solid #ddd;
}

table th {
  background: #f8f9fa;
  font-weight: bold;
  color: #333;
}

table tr:hover {
  background: #f5f5f5;
}

.price-up {
  color: #f44336;
}

.price-down {
  color: #4caf50;
}

.modal {
  position: fixed;
  z-index: 1000;
  left: 0;
  top: 0;
  width: 100%;
  height: 100%;
  background-color: rgba(0,0,0,0.5);
  display: flex;
  align-items: center;
  justify-content: center;
}

.modal-content {
  background: white;
  border-radius: 8px;
  width: 90%;
  max-width: 500px;
  box-shadow: 0 4px 20px rgba(0,0,0,0.3);
}

.modal-header {
  padding: 20px 25px 15px;
  border-bottom: 1px solid #eee;
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.modal-header h3 {
  margin: 0;
}

.close {
  color: #aaa;
  font-size: 28px;
  font-weight: bold;
  cursor: pointer;
}

.close:hover {
  color: #000;
}

.modal-body {
  padding: 20px 25px;
}

.modal-footer {
  padding: 15px 25px 20px;
  border-top: 1px solid #eee;
  display: flex;
  gap: 10px;
  justify-content: flex-end;
}

@media (max-width: 768px) {
  .content {
    padding: 15px;
  }
  
  .form-grid {
    grid-template-columns: 1fr;
  }
  
  .template-controls {
    flex-direction: column;
    align-items: stretch;
  }
  
  .template-controls select {
    width: 100%;
  }
}
</style>
