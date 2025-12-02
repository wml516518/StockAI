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

      <!-- 短线热点策略 -->
      <div class="card short-term-card">
        <div class="short-term-header">
          <div>
            <h3>热点题材成交量放大策略 <small class="short-term-subtitle">(短线操作)</small></h3>
            <p class="short-term-desc">
              调用 AKShare 热门题材 + 成交量放大模型，一键锁定当日最强的短线龙头，满足短线交易的五大条件。
            </p>
          </div>
          <span class="strategy-badge">短线</span>
        </div>
        <div class="short-term-controls">
          <label>
            热点个股范围
            <input v-model.number="shortTermParams.topHot" type="number" min="10" max="200" step="5">
          </label>
          <label>
            题材数量
            <input v-model.number="shortTermParams.topThemes" type="number" min="1" max="10">
          </label>
          <label>
            每题材入选
            <input v-model.number="shortTermParams.themeMembers" type="number" min="1" max="10">
          </label>
        </div>
        <div class="short-term-actions">
          <button class="btn btn-accent" @click="fetchShortTermStrategy" :disabled="shortTermLoading">
            {{ shortTermLoading ? '⚡️ 正在扫描热点...' : '🔥 执行短线选股' }}
          </button>
          <span class="short-term-note">本策略定位为 <strong>短线操作</strong>，建议结合风险控制与回测结果参考。</span>
        </div>
        <div v-if="shortTermLoading" class="loading">⚡️ 正在获取 AKShare 热点数据...</div>
        <div v-else-if="shortTermError" class="warning">{{ shortTermError }}</div>
        <div v-else-if="shortTermData" class="short-term-result">
          <div class="short-term-summary">
            <div>共 <strong>{{ shortTermData.resultCount || shortTermDisplayResults.length }}</strong> 只候选股</div>
            <div>满足全部条件：<strong>{{ shortTermData.passedCount || shortTermWinners.length }}</strong> 只</div>
            <div>生成时间：{{ formatDateTime(shortTermData.generatedAt) }}</div>
          </div>
          <div v-if="shortTermWinners.length" class="short-term-winners">
            <strong>策略通过（短线龙头）：</strong>
            <span v-for="winner in shortTermWinners" :key="winner.stockCode" class="winner-tag">
              {{ winner.stockCode }} {{ winner.stockName }} · {{ winner.themeName }}
            </span>
          </div>
          <div class="results-table">
            <table>
              <thead>
                <tr>
                  <th>股票</th>
                  <th>题材/位次</th>
                  <th>收盘价</th>
                  <th>涨跌幅</th>
                  <th>换手率</th>
                  <th>量能放大倍数</th>
                  <th>状态</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="stock in shortTermDisplayResults"
                  :key="stock.stockCode + stock.themeName"
                  :class="{ 'short-term-pass': stock.passed }"
                >
                  <td>
                    {{ stock.stockCode }}
                    <div class="muted">{{ stock.stockName }}</div>
                  </td>
                  <td>
                    {{ stock.themeName }}
                    <div class="muted">题材#{{ stock.themeRank }} / 成员#{{ stock.themeMemberRank }}</div>
                  </td>
                  <td>{{ stock.close ? Number(stock.close).toFixed(2) : '-' }}</td>
                  <td :class="getPriceClass(stock.pctChange)">{{ formatPercent(stock.pctChange) }}</td>
                  <td>{{ formatPercent(stock.turnoverPercent) }}</td>
                  <td>{{ stock.volumeRatio ? 'x' + Number(stock.volumeRatio).toFixed(2) : '-' }}</td>
                  <td>
                    <span v-if="stock.passed" class="badge badge-pass">满足策略</span>
                    <span v-else class="muted">{{ stock.failReason || '待观察' }}</span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <div class="short-term-hint">仅展示前 {{ shortTermDisplayResults.length }} 条结果，更多可通过接口获取。</div>
        </div>
        <div v-else class="short-term-placeholder">
          点击上方按钮即可加载短线策略结果。
        </div>
      </div>

      <!-- AI选股 -->
      <div class="card ai-screen-card">
        <div class="ai-screen-header">
          <div>
            <h3>🤖 AI智能选股</h3>
            <p class="ai-screen-desc">
              用自然语言描述你的选股条件，AI会自动理解并帮你筛选股票。例如："换手率大于5%的股票"、"价格在10到50元之间，涨跌幅在-5%到10%之间"。
            </p>
          </div>
          <span class="ai-badge">AI</span>
        </div>
        <div class="ai-screen-input">
          <textarea
            v-model="aiNaturalLanguage"
            placeholder="请输入选股条件，例如：换手率大于5%的股票、上海市场，价格在10到50元之间..."
            rows="3"
            class="ai-input-textarea"
            :disabled="aiLoading"
          ></textarea>
        </div>
        <div class="ai-screen-actions">
          <button class="btn btn-ai" @click="handleAISearch" :disabled="aiLoading || !aiNaturalLanguage.trim()">
            {{ aiLoading ? '🤖 AI正在分析中...' : '🚀 执行AI选股' }}
          </button>
          <button class="btn btn-secondary" @click="clearAIConditions" :disabled="aiLoading">🧹 清空</button>
          <span class="ai-screen-note">AI会理解你的自然语言描述，自动提取选股条件并执行筛选。</span>
        </div>
        <div v-if="aiLoading" class="loading">
          <div>🤖 AI正在解析你的选股条件，请稍候...</div>
          <div style="font-size: 0.9em; color: #666; margin-top: 10px;">
            这可能需要几秒钟时间
          </div>
        </div>
        <div v-else-if="aiError" class="warning">{{ aiError }}</div>
        <div v-else-if="aiParsedCriteria" class="ai-parsed-criteria">
          <strong>AI解析的条件：</strong>
          <div class="parsed-criteria-list">
            <span v-if="aiParsedCriteria.market" class="criteria-tag">
              市场：{{ aiParsedCriteria.market === 'SH' ? '上海' : aiParsedCriteria.market === 'SZ' ? '深圳' : aiParsedCriteria.market }}
            </span>
            <span v-if="aiParsedCriteria.minPrice !== null && aiParsedCriteria.minPrice !== undefined" class="criteria-tag">
              最低价：{{ aiParsedCriteria.minPrice }}元
            </span>
            <span v-if="aiParsedCriteria.maxPrice !== null && aiParsedCriteria.maxPrice !== undefined" class="criteria-tag">
              最高价：{{ aiParsedCriteria.maxPrice }}元
            </span>
            <span v-if="aiParsedCriteria.minChangePercent !== null && aiParsedCriteria.minChangePercent !== undefined" class="criteria-tag">
              最低涨跌幅：{{ aiParsedCriteria.minChangePercent }}%
            </span>
            <span v-if="aiParsedCriteria.maxChangePercent !== null && aiParsedCriteria.maxChangePercent !== undefined" class="criteria-tag">
              最高涨跌幅：{{ aiParsedCriteria.maxChangePercent }}%
            </span>
            <span v-if="aiParsedCriteria.minTurnoverRate !== null && aiParsedCriteria.minTurnoverRate !== undefined" class="criteria-tag">
              最低换手率：{{ aiParsedCriteria.minTurnoverRate }}%
            </span>
            <span v-if="aiParsedCriteria.maxTurnoverRate !== null && aiParsedCriteria.maxTurnoverRate !== undefined" class="criteria-tag">
              最高换手率：{{ aiParsedCriteria.maxTurnoverRate }}%
            </span>
            <span v-if="aiParsedCriteria.minPE !== null && aiParsedCriteria.minPE !== undefined" class="criteria-tag">
              最低PE：{{ aiParsedCriteria.minPE }}
            </span>
            <span v-if="aiParsedCriteria.maxPE !== null && aiParsedCriteria.maxPE !== undefined" class="criteria-tag">
              最高PE：{{ aiParsedCriteria.maxPE }}
            </span>
            <span v-if="aiParsedCriteria.minPB !== null && aiParsedCriteria.minPB !== undefined" class="criteria-tag">
              最低PB：{{ aiParsedCriteria.minPB }}
            </span>
            <span v-if="aiParsedCriteria.maxPB !== null && aiParsedCriteria.maxPB !== undefined" class="criteria-tag">
              最高PB：{{ aiParsedCriteria.maxPB }}
            </span>
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
          <button class="btn btn-auto-selection" @click="handleAutoSelection" :disabled="autoSelectionLoading">
            {{ autoSelectionLoading ? '🤖 自动选股中...' : '🤖 执行自动选股' }}
          </button>
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
        <div v-else-if="results.length === 0 && !hasSearched" class="loading">等待查询...</div>
        <div v-else-if="results.length === 0 && hasSearched" class="loading">
          <p class="warning">⚠️ 未找到符合条件的股票</p>
        </div>
        <div v-else>
          <!-- 分页信息 -->
          <div class="pagination-info">
            <strong>找到 {{ totalCount }} 只股票</strong>
            <span class="page-info">
              第 <strong>{{ currentPage }}</strong> / <strong>{{ totalPages }}</strong> 页，每页 <strong>{{ pageSize }}</strong> 条
            </span>
          </div>

          <div class="bulk-actions">
            <label class="bulk-checkbox">
              <input type="checkbox" :checked="isAllSelected" @change="toggleSelectAll">
              全选
            </label>
            <span class="bulk-summary">已选 {{ selectedCount }} / {{ results.length }}</span>
            <select v-model="bulkCategoryId" class="bulk-select">
              <option value="">选择目标分类</option>
              <option
                v-for="cat in watchlistCategories"
                :key="cat.id || cat.Id"
                :value="cat.id || cat.Id"
              >
                {{ cat.name || cat.Name }}
              </option>
            </select>
            <button
              class="btn btn-small"
              @click="handleBulkAddToWatchlist"
              :disabled="selectedCount === 0 || bulkAdding || !bulkCategoryId"
            >
              {{ bulkAdding ? '加入中...' : '批量加入自选' }}
            </button>
            <span class="bulk-message" v-if="bulkMessage">{{ bulkMessage }}</span>
          </div>
          
          <!-- 结果表格 -->
          <div class="results-table">
            <table>
              <thead>
                <tr>
                  <th style="width: 48px;">
                    <input
                      type="checkbox"
                      :checked="isAllSelected"
                      @change="toggleSelectAll"
                      aria-label="全选"
                    >
                  </th>
                  <th>股票代码</th>
                  <th>股票名称</th>
                  <th>当前价</th>
                  <th>涨跌幅</th>
                  <th>换手率</th>
                  <th>市盈率</th>
                  <th>市净率</th>
                  <th>成交量</th>
                  <th v-if="autoSelectionResults">AI评分</th>
                  <th v-if="autoSelectionResults">行业</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="stock in results" :key="stock.code">
                  <td>
                    <input
                      type="checkbox"
                      :checked="isSelected(stock.code)"
                      @change="event => toggleSelectStock(event, stock.code)"
                      aria-label="选择股票"
                    >
                  </td>
                  <td>{{ stock.code }}</td>
                  <td>{{ stock.name || '-' }}</td>
                  <td>{{ formatPrice(stock.currentPrice) }}</td>
                  <td :class="getPriceClass(stock.changePercent)">
                    {{ formatPercent(stock.changePercent) }}
                  </td>
                  <td>{{ formatPercent(stock.turnoverRate) }}</td>
                  <td>{{ stock.pe ? stock.pe.toFixed(2) : '-' }}</td>
                  <td>{{ stock.pb ? stock.pb.toFixed(2) : '-' }}</td>
                  <td>{{ formatVolume(stock.volume) }}</td>
                  <td v-if="autoSelectionResults">
                    <span class="ai-score" :class="getAIScoreClass(stock.aiScore)">
                      {{ stock.aiScore !== undefined ? stock.aiScore : '-' }}
                    </span>
                  </td>
                  <td v-if="autoSelectionResults">{{ stock.industryName || '-' }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          
          <!-- 自动选股结果摘要 -->
          <div v-if="autoSelectionResults" class="auto-selection-summary">
            <div class="summary-item">
              <strong>总股票数：</strong>{{ autoSelectionResults.totalStocks }}
            </div>
            <div class="summary-item">
              <strong>筛选后：</strong>{{ autoSelectionResults.filteredCount }}
            </div>
            <div class="summary-item">
              <strong>AI评分：</strong>{{ autoSelectionResults.scoredCount }}
            </div>
            <div class="summary-item">
              <strong>最终选中：</strong>{{ autoSelectionResults.selectedCount }}
            </div>
          </div>
          
          <!-- 分页控件 -->
          <div class="pagination-controls" v-if="totalPages > 0">
            <button 
              class="pagination-btn" 
              :disabled="currentPage === 1" 
              @click="goToPage(1)"
              title="首页"
            >
              « 首页
            </button>
            <button 
              class="pagination-btn" 
              :disabled="currentPage === 1" 
              @click="goToPage(currentPage - 1)"
              title="上一页"
            >
              ‹ 上一页
            </button>
            
            <!-- 页码按钮 -->
            <template v-if="totalPages > 0">
              <template v-if="startPage > 1">
                <button class="pagination-btn" @click="goToPage(1)">1</button>
                <span v-if="startPage > 2" class="pagination-ellipsis">...</span>
              </template>
              
              <button
                v-for="page in visiblePages"
                :key="page"
                class="pagination-btn"
                :class="{ active: page === currentPage }"
                @click="goToPage(page)"
              >
                {{ page }}
              </button>
              
              <template v-if="endPage < totalPages">
                <span v-if="endPage < totalPages - 1" class="pagination-ellipsis">...</span>
                <button class="pagination-btn" @click="goToPage(totalPages)">{{ totalPages }}</button>
              </template>
            </template>
            
            <button 
              class="pagination-btn" 
              :disabled="currentPage === totalPages" 
              @click="goToPage(currentPage + 1)"
              title="下一页"
            >
              下一页 ›
            </button>
            <button 
              class="pagination-btn" 
              :disabled="currentPage === totalPages" 
              @click="goToPage(totalPages)"
              title="末页"
            >
              末页 »
            </button>
            
            <!-- 每页数量选择 -->
            <span class="page-size-selector">
              每页：
              <select :value="pageSize" @change="onPageSizeChange" class="page-size-select">
                <option :value="10">10</option>
                <option :value="20">20</option>
                <option :value="50">50</option>
              </select>
            </span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onActivated, watch } from 'vue'
import api from '../services/api'
import { screenTemplateService } from '../services/screenTemplateService'
import { screenService } from '../services/screenService'
import { useWatchlistStore } from '../stores/watchlist'

const loading = ref(false)
const results = ref([])
const templates = ref([])
const selectedTemplateId = ref('')
const showSaveDialog = ref(false)
const editingTemplateId = ref(null)
const hasSearched = ref(false)

// 自动选股相关状态
const autoSelectionLoading = ref(false)
const autoSelectionResults = ref(null)

// 分页相关状态
const currentPage = ref(1)
const pageSize = ref(10)
const totalCount = ref(0)
const totalPages = ref(0)

// 保存上一次的查询条件，用于判断是否需要强制刷新
const lastSearchCriteria = ref(null)

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

const selectedStockCodes = ref([])
const bulkCategoryId = ref('')
const bulkAdding = ref(false)
const bulkMessage = ref('')
const shortTermParams = ref({
  topHot: 60,
  topThemes: 3,
  themeMembers: 3
})
const shortTermData = ref(null)
const shortTermLoading = ref(false)
const shortTermError = ref('')

// AI选股相关状态
const aiNaturalLanguage = ref('')
const aiLoading = ref(false)
const aiError = ref('')
const aiParsedCriteria = ref(null)
const isAISearchMode = ref(false) // 标记当前是否使用AI选股模式

const watchlistStore = useWatchlistStore()
const watchlistCategories = computed(() => watchlistStore.categories || [])

const selectedCount = computed(() => selectedStockCodes.value.length)
const isAllSelected = computed(() => {
  if (!results.value.length) return false
  return selectedStockCodes.value.length === results.value.length
})

onMounted(async () => {
  await loadTemplates()
  await watchlistStore.fetchCategories()
  initBulkCategory()
})

onActivated(async () => {
  await loadTemplates()
  await watchlistStore.fetchCategories()
  initBulkCategory()
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

const handleScreen = async (pageIndex = 1) => {
  loading.value = true
  hasSearched.value = true
  currentPage.value = pageIndex
  isAISearchMode.value = false // 清除AI选股模式，使用普通选股
  
  try {
    // 清理null值，转换为undefined或空字符串
    const cleanCriteria = {}
    
    // 处理market字段（空字符串转为null）
    if (criteria.value.market && criteria.value.market.trim() !== '') {
      cleanCriteria.market = criteria.value.market
    }
    
    // 处理数值字段（null转为undefined，不发送）
    const numberFields = [
      'minPrice', 'maxPrice', 'minChangePercent', 'maxChangePercent',
      'minTurnoverRate', 'maxTurnoverRate', 'minVolume', 'maxVolume',
      'minMarketValue', 'maxMarketValue', 'minPE', 'maxPE',
      'minPB', 'maxPB', 'minDividendYield', 'maxDividendYield'
    ]
    
    numberFields.forEach(field => {
      const value = criteria.value[field]
      if (value !== null && value !== undefined && value !== '') {
        cleanCriteria[field] = Number(value)
      }
    })
    
    // 判断查询条件是否改变（排除分页参数）
    const currentCriteriaKey = JSON.stringify(cleanCriteria)
    const criteriaChanged = lastSearchCriteria.value !== currentCriteriaKey
    
    // 构建带分页的请求数据（确保数据类型正确）
    const criteriaWithPagination = {
      ...cleanCriteria,
      pageIndex: Number(pageIndex) || 1, // 确保是数字类型
      pageSize: Number(pageSize.value) || 10, // 确保是数字类型
      forceRefresh: criteriaChanged // 只有查询条件改变时才强制刷新
    }
    
    // 如果查询条件改变，更新保存的条件
    if (criteriaChanged) {
      lastSearchCriteria.value = currentCriteriaKey
    }
    
    // 验证数据类型
    if (isNaN(criteriaWithPagination.pageIndex) || criteriaWithPagination.pageIndex < 1) {
      criteriaWithPagination.pageIndex = 1
    }
    if (isNaN(criteriaWithPagination.pageSize) || criteriaWithPagination.pageSize < 1) {
      criteriaWithPagination.pageSize = 10
    }
    
    console.log('发送选股请求:', criteriaWithPagination)
    console.log('数据类型检查:', {
      pageIndex: typeof criteriaWithPagination.pageIndex,
      pageSize: typeof criteriaWithPagination.pageSize,
      pageIndexValue: criteriaWithPagination.pageIndex,
      pageSizeValue: criteriaWithPagination.pageSize
    })
    
    // 选股操作可能需要较长时间，设置更长的超时时间（5分钟）
    const response = await api.post('/screen/search', criteriaWithPagination, {
      timeout: 300000 // 5分钟 = 300000毫秒
    })
    
    // 处理分页响应
    results.value = response?.items || []
    totalCount.value = response?.totalCount || 0
    currentPage.value = response?.pageIndex || pageIndex
    pageSize.value = response?.pageSize || pageSize.value
    totalPages.value = response?.totalPages || Math.max(1, Math.ceil(totalCount.value / pageSize.value))
  } catch (error) {
    console.error('选股失败:', error)
    console.error('错误详情:', {
      status: error.response?.status,
      statusText: error.response?.statusText,
      data: error.response?.data,
      message: error.message
    })
    
    if (error.code === 'ECONNABORTED' || error.message?.includes('timeout')) {
      alert('选股超时：查询时间过长，请尝试缩小筛选条件范围或减少查询数量。')
    } else if (error.response?.status === 400) {
      // 400错误通常是请求格式问题
      const errorMsg = error.response?.data?.message || error.response?.data?.error || '请求格式错误'
      const errors = error.response?.data?.errors
      let fullErrorMsg = `选股失败 (400): ${errorMsg}`
      if (errors) {
        fullErrorMsg += '\n\n详细错误:\n' + JSON.stringify(errors, null, 2)
      }
      console.error('400错误详情:', fullErrorMsg)
      alert(fullErrorMsg)
    } else {
      alert('选股失败: ' + (error.response?.data?.message || error.response?.data?.error || error.message))
    }
    results.value = []
    totalCount.value = 0
    totalPages.value = 0
  } finally {
    loading.value = false
  }
}

const fetchShortTermStrategy = async () => {
  shortTermLoading.value = true
  shortTermError.value = ''
  try {
    const response = await screenService.fetchShortTermHotStrategy({
      topHot: shortTermParams.value.topHot,
      topThemes: shortTermParams.value.topThemes,
      themeMembers: shortTermParams.value.themeMembers
    })
    shortTermData.value = response
  } catch (error) {
    console.error('获取短线策略失败:', error)
    shortTermError.value = error.response?.data?.message || error.message || '获取短线策略失败'
  } finally {
    shortTermLoading.value = false
  }
}

const handleAISearch = async (pageIndex = 1) => {
  if (!aiNaturalLanguage.value.trim()) {
    alert('请输入选股条件')
    return
  }

  aiLoading.value = true
  aiError.value = ''
  aiParsedCriteria.value = null
  hasSearched.value = true
  currentPage.value = pageIndex
  isAISearchMode.value = true // 设置AI选股模式

  try {
    console.log('准备发送AI选股请求:', {
      naturalLanguage: aiNaturalLanguage.value.trim(),
      pageIndex: pageIndex,
      pageSize: pageSize.value
    })
    
    const response = await screenService.aiSearch(
      aiNaturalLanguage.value.trim(),
      pageIndex,
      pageSize.value
    )

    // 处理分页响应
    results.value = response?.items || []
    totalCount.value = response?.totalCount || 0
    currentPage.value = response?.pageIndex || pageIndex
    pageSize.value = response?.pageSize || pageSize.value
    totalPages.value = response?.totalPages || Math.max(1, Math.ceil(totalCount.value / pageSize.value))

    // 尝试从响应中提取解析的条件（如果后端返回了的话）
    // 这里我们可以通过反向工程来显示AI解析的条件
    // 或者后端可以在响应中包含解析的条件信息
    // 暂时留空，后续可以扩展

    // 滚动到结果区域
    setTimeout(() => {
      const resultsCard = document.querySelector('.card:has(.results-table)')
      if (resultsCard) {
        resultsCard.scrollIntoView({ behavior: 'smooth', block: 'start' })
      }
    }, 100)
  } catch (error) {
    console.error('AI选股失败:', error)
    console.error('错误详情:', {
      status: error.response?.status,
      statusText: error.response?.statusText,
      data: error.response?.data,
      message: error.message
    })
    
    // 提取错误信息
    let errorMessage = 'AI选股失败'
    if (error.response?.data) {
      const errorData = error.response.data
      if (typeof errorData === 'string') {
        errorMessage = errorData
      } else if (errorData.message) {
        errorMessage = errorData.message
      } else if (errorData.error) {
        errorMessage = errorData.error
      } else if (errorData.details && Array.isArray(errorData.details)) {
        errorMessage = `请求格式错误: ${errorData.details.join(', ')}`
      }
    } else if (error.message) {
      errorMessage = error.message
    }
    
    if (error.code === 'ECONNABORTED' || error.message?.includes('timeout')) {
      errorMessage = 'AI选股超时：解析时间过长，请尝试简化选股条件或稍后重试。'
    } else if (error.response?.status === 400) {
      if (!errorMessage.includes('请求格式错误')) {
        errorMessage = `AI选股失败 (400): ${errorMessage}`
      }
    }
    
    aiError.value = errorMessage
    results.value = []
    totalCount.value = 0
    totalPages.value = 0
  } finally {
    aiLoading.value = false
  }
}

const clearAIConditions = () => {
  aiNaturalLanguage.value = ''
  aiError.value = ''
  aiParsedCriteria.value = null
  results.value = []
  hasSearched.value = false
  currentPage.value = 1
  totalCount.value = 0
  totalPages.value = 0
  isAISearchMode.value = false // 清除AI选股模式
}

const goToPage = (page) => {
  if (page >= 1 && page <= totalPages.value && page !== currentPage.value) {
    // 根据当前模式调用相应的方法
    if (isAISearchMode.value) {
      handleAISearch(page)
    } else {
      handleScreen(page)
    }
  }
}

const onPageSizeChange = (event) => {
  // 改变每页数量时，重新从第一页开始查询
  const newSize = Number(event.target.value) || 10
  pageSize.value = newSize
  // 根据当前模式调用相应的方法
  if (isAISearchMode.value) {
    handleAISearch(1)
  } else {
    handleScreen(1)
  }
}

// 计算可见页码范围
const startPage = computed(() => {
  return Math.max(1, currentPage.value - 2)
})

const endPage = computed(() => {
  return Math.min(totalPages.value, currentPage.value + 2)
})

const visiblePages = computed(() => {
  const pages = []
  for (let i = startPage.value; i <= endPage.value; i++) {
    pages.push(i)
  }
  return pages
})

const shortTermDisplayResults = computed(() => {
  if (!shortTermData.value?.results) return []
  return shortTermData.value.results.slice(0, 12)
})

const shortTermWinners = computed(() => {
  if (!shortTermData.value?.results) return []
  return shortTermData.value.results.filter(item => item.passed)
})

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
  hasSearched.value = false
  currentPage.value = 1
  totalCount.value = 0
  totalPages.value = 0
  lastSearchCriteria.value = null // 清空保存的查询条件
  isAISearchMode.value = false // 清除AI选股模式
  autoSelectionResults.value = null // 清空自动选股结果
}

// 手动执行自动选股服务
const handleAutoSelection = async () => {
  // 防止重复调用
  if (autoSelectionLoading.value) {
    console.warn('自动选股正在执行中，请勿重复点击')
    return
  }
  
  autoSelectionLoading.value = true
  autoSelectionResults.value = null
  hasSearched.value = true
  
  try {
    const response = await api.post('/screen/auto-selection/execute', {}, {
      timeout: 1200000 // 15分钟超时，因为AI评分可能需要较长时间（已优化为并行处理）
    })
    
    autoSelectionResults.value = response
    
    // 将自动选股结果转换为普通选股结果格式，显示在页面上
    if (response.selectedStocks && response.selectedStocks.length > 0) {
      results.value = response.selectedStocks.map(stock => ({
        code: stock.stockCode,
        name: stock.stockName,
        currentPrice: stock.currentPrice,
        changePercent: stock.changePercent,
        turnoverRate: stock.turnoverRate,
        volume: stock.volume,
        pe: null,
        pb: null,
        aiScore: stock.aiScore,
        industryName: stock.industryName,
        hotRank: stock.hotRank
      }))
      
      totalCount.value = response.selectedCount
      currentPage.value = 1
      pageSize.value = 10
      totalPages.value = Math.max(1, Math.ceil(totalCount.value / pageSize.value))
      
      // 滚动到结果区域
      setTimeout(() => {
        const resultsCard = document.querySelector('.card:has(.results-table)')
        if (resultsCard) {
          resultsCard.scrollIntoView({ behavior: 'smooth', block: 'start' })
        }
      }, 100)
    } else {
      results.value = []
      totalCount.value = 0
      totalPages.value = 0
      alert('自动选股未找到符合条件的股票')
    }
  } catch (error) {
    console.error('执行自动选股失败:', error)
    alert('执行自动选股失败: ' + (error.response?.data?.message || error.message))
    results.value = []
    totalCount.value = 0
    totalPages.value = 0
  } finally {
    autoSelectionLoading.value = false
  }
}

const formatPrice = (price) => {
  if (price === null || price === undefined) return '-'
  return Number(price).toFixed(2)
}

const formatPercent = (percent) => {
  if (percent === null || percent === undefined) return '-'
  return (percent > 0 ? '+' : '') + Number(percent).toFixed(2) + '%'
}

const formatVolume = (volume) => {
  if (volume === null || volume === undefined) return '-'
  return (volume / 10000).toFixed(2) + '万手'
}

const formatDateTime = (value) => {
  if (!value) return '-'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return value
  }
  return date.toLocaleString()
}

const getPriceClass = (value) => {
  if (!value) return ''
  return value > 0 ? 'price-up' : value < 0 ? 'price-down' : ''
}

const getAIScoreClass = (score) => {
  if (score === undefined || score === null) return ''
  if (score >= 8) return 'ai-score-high'
  if (score >= 6) return 'ai-score-medium'
  return 'ai-score-low'
}

const toggleSelectAll = (event) => {
  const checked = event.target.checked
  if (!checked) {
    selectedStockCodes.value = []
    return
  }
  selectedStockCodes.value = results.value.map(stock => stock.code)
}

const toggleSelectStock = (event, stockCode) => {
  const checked = event.target.checked
  if (checked) {
    if (!selectedStockCodes.value.includes(stockCode)) {
      selectedStockCodes.value = [...selectedStockCodes.value, stockCode]
    }
  } else {
    selectedStockCodes.value = selectedStockCodes.value.filter(code => code !== stockCode)
  }
}

const isSelected = (stockCode) => selectedStockCodes.value.includes(stockCode)

const initBulkCategory = () => {
  if (watchlistCategories.value.length === 0) {
    bulkCategoryId.value = ''
    return
  }
  const existing = watchlistCategories.value.find(cat => (cat.id || cat.Id || '').toString() === bulkCategoryId.value)
  if (!existing) {
    const first = watchlistCategories.value[0]
    bulkCategoryId.value = first ? String(first.id || first.Id || '') : ''
  }
}

const handleBulkAddToWatchlist = async () => {
  if (selectedStockCodes.value.length === 0) {
    alert('请先勾选需要加入自选的股票')
    return
  }
  if (!bulkCategoryId.value) {
    alert('请选择目标分类')
    return
  }

  const categoryId = Number(bulkCategoryId.value)
  bulkAdding.value = true
  bulkMessage.value = ''

  let successCount = 0
  const failureMessages = []

  for (const code of selectedStockCodes.value) {
    try {
      await watchlistStore.addStock(code, categoryId)
      successCount++
    } catch (error) {
      const message =
        error?.response?.data ||
        error?.message ||
        '加入自选失败'
      failureMessages.push(`${code}: ${message}`)
    }
  }

  if (successCount > 0) {
    await watchlistStore.fetchWatchlist()
  }

  const summary = []
  summary.push(`成功加入 ${successCount} 只股票`)
  if (failureMessages.length) {
    summary.push(`失败 ${failureMessages.length} 只`)
  }
  bulkMessage.value = summary.join('，')

  if (failureMessages.length) {
    console.warn('批量加入自选失败详情:', failureMessages)
  }

  bulkAdding.value = false
}

watch(results, () => {
  selectedStockCodes.value = []
  bulkMessage.value = ''
})

watch(watchlistCategories, () => {
  initBulkCategory()
})
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

.btn-auto-selection {
  background: linear-gradient(90deg, #ff9800, #ffb74d);
  color: white;
  border: none;
  box-shadow: 0 2px 4px rgba(255, 152, 0, 0.3);
}

.btn-auto-selection:hover:not(:disabled) {
  background: linear-gradient(90deg, #f57c00, #ff9800);
  box-shadow: 0 4px 8px rgba(255, 152, 0, 0.4);
}

.btn-auto-selection:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.auto-selection-summary {
  display: flex;
  gap: 20px;
  padding: 15px;
  background: #fff8e1;
  border: 1px solid #ffcc80;
  border-radius: 6px;
  margin-top: 15px;
  flex-wrap: wrap;
}

.summary-item {
  font-size: 0.95em;
  color: #e65100;
}

.summary-item strong {
  color: #bf360c;
}

.ai-score {
  font-weight: bold;
  padding: 4px 8px;
  border-radius: 4px;
  display: inline-block;
}

.ai-score-high {
  background: #c8e6c9;
  color: #2e7d32;
}

.ai-score-medium {
  background: #fff9c4;
  color: #f57f17;
}

.ai-score-low {
  background: #ffcdd2;
  color: #c62828;
}

.results-table {
  margin-top: 20px;
  overflow-x: auto;
}

table {
  width: 100%;
  border-collapse: collapse;
}

table th:first-child,
table td:first-child {
  text-align: center;
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

.bulk-actions {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 12px;
  background: #f4f6ff;
  border: 1px solid #d8defd;
  border-radius: 6px;
  margin-bottom: 12px;
  flex-wrap: wrap;
}

.bulk-checkbox {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-weight: 500;
}

.bulk-summary {
  color: #374151;
  font-size: 0.9em;
}

.bulk-select {
  min-width: 160px;
  padding: 6px 10px;
  border: 1px solid #d0d7ff;
  border-radius: 4px;
  font-size: 0.9em;
  background: #fff;
}

.bulk-message {
  font-size: 0.9em;
  color: #2563eb;
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

.pagination-info {
  padding: 10px 15px;
  background: #f5f5f5;
  border-radius: 4px;
  margin-bottom: 15px;
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
}

.page-info {
  color: #666;
  font-size: 0.95em;
}

.page-info strong {
  color: #007bff;
}

.pagination-controls {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  flex-wrap: wrap;
  padding: 15px;
  background: #f9f9f9;
  border-radius: 6px;
  border: 1px solid #e0e0e0;
  margin-top: 20px;
}

.pagination-btn {
  padding: 8px 12px;
  margin: 0 2px;
  border: 1px solid #ddd;
  background: white;
  color: #333;
  border-radius: 4px;
  cursor: pointer;
  font-size: 14px;
  transition: all 0.2s;
  min-width: 40px;
  text-align: center;
}

.pagination-btn:hover:not(:disabled):not(.active) {
  background: #f0f0f0;
  border-color: #007bff;
  color: #007bff;
}

.pagination-btn.active {
  background: #007bff;
  color: white;
  border-color: #007bff;
  font-weight: bold;
}

.pagination-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
  background: #f5f5f5;
  color: #999;
}

.pagination-btn:disabled:hover {
  background: #f5f5f5;
  border-color: #ddd;
  color: #999;
}

.pagination-ellipsis {
  padding: 8px 4px;
  color: #666;
  font-size: 14px;
  user-select: none;
}

.page-size-selector {
  margin-left: 15px;
  color: #666;
  font-size: 0.9em;
  display: flex;
  align-items: center;
  gap: 5px;
}

.page-size-select {
  padding: 5px 8px;
  border: 1px solid #ddd;
  border-radius: 4px;
  cursor: pointer;
  background: white;
}

.warning {
  color: #ff9800;
  font-weight: bold;
}

.short-term-card {
  border: 1px solid #ffe2cc;
  background: linear-gradient(135deg, #fff8f2 0%, #fff 100%);
}

.short-term-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 16px;
  margin-bottom: 12px;
}

.short-term-subtitle {
  font-weight: normal;
  font-size: 0.8em;
  color: #ff7a18;
}

.short-term-desc {
  margin: 4px 0 0;
  color: #6b7280;
  line-height: 1.4;
}

.strategy-badge {
  background: #ff7a18;
  color: #fff;
  padding: 6px 12px;
  border-radius: 999px;
  font-size: 0.85em;
  font-weight: 600;
  white-space: nowrap;
}

.short-term-controls {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 12px;
}

.short-term-controls label {
  flex: 1;
  min-width: 150px;
  display: flex;
  flex-direction: column;
  font-size: 0.9em;
  color: #374151;
  font-weight: 600;
}

.short-term-controls input {
  margin-top: 4px;
  padding: 6px 10px;
  border: 1px solid #f7b58c;
  border-radius: 4px;
}

.short-term-actions {
  display: flex;
  align-items: center;
  gap: 16px;
  flex-wrap: wrap;
  margin-bottom: 12px;
}

.btn-accent {
  background: linear-gradient(90deg, #ff7a18, #ffb347);
  color: white;
  border: none;
  box-shadow: 0 4px 10px rgba(255, 122, 24, 0.3);
}

.short-term-note {
  color: #6b7280;
  font-size: 0.9em;
}

.short-term-result {
  border-top: 1px dashed #ffd0a8;
  padding-top: 12px;
}

.short-term-summary {
  display: flex;
  gap: 16px;
  flex-wrap: wrap;
  font-size: 0.95em;
  color: #374151;
  margin-bottom: 10px;
}

.short-term-winners {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  font-size: 0.9em;
  margin-bottom: 10px;
}

.winner-tag {
  background: #fff;
  border: 1px solid #ffdab5;
  padding: 4px 8px;
  border-radius: 999px;
  color: #b45309;
  font-weight: 600;
}

.muted {
  color: #6b7280;
  font-size: 0.85em;
}

.short-term-pass {
  background: rgba(255, 204, 153, 0.25);
}

.badge {
  display: inline-flex;
  align-items: center;
  padding: 4px 8px;
  border-radius: 6px;
  font-size: 0.8em;
  font-weight: 600;
}

.badge-pass {
  background: #16a34a;
  color: white;
}

.short-term-hint {
  margin-top: 8px;
  font-size: 0.85em;
  color: #9ca3af;
}

.short-term-placeholder {
  padding: 12px;
  background: #fffdf8;
  border: 1px dashed #ffe0bf;
  border-radius: 6px;
  color: #9ca3af;
  text-align: center;
}

.ai-screen-card {
  border: 1px solid #c7d2fe;
  background: linear-gradient(135deg, #f0f4ff 0%, #fff 100%);
}

.ai-screen-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 16px;
  margin-bottom: 12px;
}

.ai-screen-desc {
  margin: 4px 0 0;
  color: #6b7280;
  line-height: 1.4;
  font-size: 0.9em;
}

.ai-badge {
  background: linear-gradient(90deg, #6366f1, #8b5cf6);
  color: #fff;
  padding: 6px 12px;
  border-radius: 999px;
  font-size: 0.85em;
  font-weight: 600;
  white-space: nowrap;
}

.ai-screen-input {
  margin-bottom: 12px;
}

.ai-input-textarea {
  width: 100%;
  padding: 12px;
  border: 1px solid #c7d2fe;
  border-radius: 6px;
  font-size: 0.95em;
  font-family: inherit;
  resize: vertical;
  transition: border-color 0.2s;
}

.ai-input-textarea:focus {
  outline: none;
  border-color: #6366f1;
  box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.1);
}

.ai-input-textarea:disabled {
  background: #f3f4f6;
  cursor: not-allowed;
}

.ai-screen-actions {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 12px;
}

.btn-ai {
  background: linear-gradient(90deg, #6366f1, #8b5cf6);
  color: white;
  border: none;
  box-shadow: 0 4px 10px rgba(99, 102, 241, 0.3);
}

.btn-ai:hover:not(:disabled) {
  background: linear-gradient(90deg, #4f46e5, #7c3aed);
  box-shadow: 0 6px 15px rgba(99, 102, 241, 0.4);
}

.ai-screen-note {
  color: #6b7280;
  font-size: 0.85em;
  flex: 1;
}

.ai-parsed-criteria {
  border-top: 1px dashed #c7d2fe;
  padding-top: 12px;
  font-size: 0.9em;
}

.parsed-criteria-list {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 8px;
}

.criteria-tag {
  background: #fff;
  border: 1px solid #c7d2fe;
  padding: 4px 10px;
  border-radius: 999px;
  color: #4f46e5;
  font-size: 0.85em;
  font-weight: 500;
}

@media (max-width: 768px) {
  .content {
    padding: 15px;
  }
  
  .bulk-actions {
    flex-direction: column;
    align-items: stretch;
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
  
  .pagination-controls {
    flex-direction: column;
    gap: 10px;
  }
  
  .pagination-info {
    flex-direction: column;
    align-items: flex-start;
  }

  .short-term-controls {
    flex-direction: column;
  }

  .short-term-summary {
    flex-direction: column;
  }
}
</style>
