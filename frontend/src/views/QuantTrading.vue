<template>
  <div class="container">
    <div class="content">
      <!-- 策略管理 -->
      <div class="card">
        <h3>📊 策略管理</h3>
        <div class="strategy-controls">
          <button class="btn" @click="loadStrategies" :disabled="loading">🔄 刷新策略</button>
          <button class="btn btn-success" @click="importDefaultStrategies" :disabled="loading">📥 导入默认策略</button>
          <button class="btn btn-info" @click="showCreateStrategy = true">➕ 创建策略</button>
        </div>
        <div v-if="loading" class="loading">加载中...</div>
        <div v-else-if="strategies.length === 0" class="loading">暂无策略，点击"导入默认策略"或"创建策略"开始</div>
        <div v-else class="strategy-list">
          <div v-for="strategy in strategies" :key="strategy.id" class="strategy-item">
            <div class="strategy-info">
              <div class="strategy-name">
                {{ strategy.name }}
                <span v-if="strategy.isActive" class="badge badge-active">启用</span>
                <span v-else class="badge badge-inactive">禁用</span>
              </div>
              <div class="strategy-desc">{{ strategy.description || '无描述' }}</div>
              <div class="strategy-meta">
                <span>类型: {{ getStrategyTypeText(strategy.type) }}</span>
                <span>初始资金: {{ formatCurrency(strategy.initialCapital) }}</span>
              </div>
            </div>
            <div class="strategy-actions">
              <button class="btn btn-small" @click="selectStrategy(strategy)">编辑</button>
              <button class="btn btn-small btn-warning" @click="toggleStrategy(strategy.id)">
                {{ strategy.isActive ? '禁用' : '启用' }}
              </button>
              <button class="btn btn-small btn-danger" @click="deleteStrategy(strategy.id)">删除</button>
            </div>
          </div>
        </div>
      </div>

      <!-- 创建/编辑策略对话框 -->
      <div v-if="showCreateStrategy || editingStrategy" class="modal" @click.self="closeStrategyDialog()">
        <div class="modal-content strategy-modal">
          <div class="modal-header">
            <h3>{{ editingStrategy ? '编辑策略' : '创建策略' }}</h3>
            <span class="close" @click="closeStrategyDialog()">&times;</span>
          </div>
          <div class="modal-body">
            <div class="form-group">
              <label>策略名称 *</label>
              <input v-model="strategyForm.name" type="text" placeholder="输入策略名称" required>
            </div>
            <div class="form-group">
              <label>策略描述</label>
              <textarea v-model="strategyForm.description" rows="3" placeholder="输入策略描述"></textarea>
            </div>
            <div class="form-group">
              <label>初始资金（元）</label>
              <input v-model.number="strategyForm.initialCapital" type="number" min="1000" step="1000">
            </div>
            <div class="form-group">
              <label>策略类型</label>
              <select v-model="strategyForm.type">
                <option value="TechnicalIndicator">技术指标策略</option>
                <option value="Fundamental">基本面策略</option>
                <option value="Arbitrage">套利策略</option>
                <option value="MachineLearning">机器学习策略</option>
                <option value="Custom">自定义策略</option>
              </select>
            </div>
            <div class="form-group">
              <label>
                <input type="checkbox" v-model="strategyForm.isActive"> 启用策略
              </label>
            </div>
            <div class="form-group">
              <label>策略参数（JSON格式）</label>
              <textarea v-model="strategyForm.parametersJson" rows="6" placeholder='{"shortPeriod": 5, "longPeriod": 20, ...}'></textarea>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn" @click="saveStrategy">💾 保存</button>
            <button class="btn btn-secondary" @click="closeStrategyDialog()">取消</button>
          </div>
        </div>
      </div>

      <!-- 回测分析 -->
      <div class="card">
        <h3>📈 回测分析</h3>
        
        <!-- 一键回测 -->
        <div class="card quick-backtest">
          <h4>🚀 新手一键回测</h4>
          <p>无需复杂配置，使用简单移动平均策略快速体验回测功能</p>
          <div class="quick-backtest-form">
            <div class="form-group">
              <label>股票代码</label>
              <input v-model="quickBacktest.stockCode" type="text" placeholder="如：000001">
            </div>
            <div class="form-group">
              <label>开始日期</label>
              <input v-model="quickBacktest.startDate" type="date">
            </div>
            <div class="form-group">
              <label>结束日期</label>
              <input v-model="quickBacktest.endDate" type="date">
            </div>
            <button class="btn btn-success" @click="runQuickBacktest" :disabled="backtestLoading">⚡ 一键回测</button>
          </div>
          <div v-if="quickBacktestResult" class="result-card">
            <div class="result-message">{{ quickBacktestResult.message }}</div>
            <div class="result-details">
              <div class="result-stats">
                <div class="stat-item">
                  <span class="stat-label">总收益率:</span>
                  <span class="stat-value" :class="getReturnClass(quickBacktestResult.totalReturn)">
                    {{ formatPercent(quickBacktestResult.totalReturn * 100) }}
                  </span>
                </div>
                <div class="stat-item">
                  <span class="stat-label">年化收益率:</span>
                  <span class="stat-value" :class="getReturnClass(quickBacktestResult.annualizedReturn * 100)">
                    {{ formatPercent(quickBacktestResult.annualizedReturn * 100) }}
                  </span>
                </div>
                <div class="stat-item">
                  <span class="stat-label">最大回撤:</span>
                  <span class="stat-value">{{ formatPercent(quickBacktestResult.maxDrawdown * 100) }}</span>
                </div>
                <div class="stat-item">
                  <span class="stat-label">夏普比率:</span>
                  <span class="stat-value">{{ quickBacktestResult.sharpeRatio?.toFixed(2) || '-' }}</span>
                </div>
                <div class="stat-item">
                  <span class="stat-label">交易次数:</span>
                  <span class="stat-value">{{ quickBacktestResult.totalTrades || 0 }}</span>
                </div>
                <div class="stat-item">
                  <span class="stat-label">胜率:</span>
                  <span class="stat-value">{{ formatPercent(quickBacktestResult.winRate * 100) }}</span>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- 批量回测 -->
        <div class="form-group">
          <label>批量回测</label>
          <div class="backtest-config">
            <div class="form-group">
              <label>股票代码（用逗号分隔）</label>
              <textarea v-model="batchBacktest.stockCodes" placeholder="输入多个股票代码，用逗号分隔，如：000001,600000" rows="3"></textarea>
            </div>
            <div class="date-config">
              <div class="form-group">
                <label>开始日期</label>
                <input v-model="batchBacktest.startDate" type="date">
              </div>
              <div class="form-group">
                <label>结束日期</label>
                <input v-model="batchBacktest.endDate" type="date">
              </div>
              <div class="form-group">
                <label>初始资金（元）</label>
                <input v-model.number="batchBacktest.initialCapital" type="number" min="1000" step="1000">
              </div>
              <button class="btn" @click="runBatchBacktest" :disabled="backtestLoading">🚀 开始批量回测</button>
            </div>
          </div>
        </div>
        <div v-if="batchBacktestResults.length > 0" class="backtest-results">
          <h4>批量回测结果</h4>
          <div class="results-grid">
            <div v-for="result in batchBacktestResults" :key="result.stockCode" class="result-item" @click="selectBacktestResult(result)">
              <div class="result-stock">{{ result.stockCode }}</div>
              <div class="result-return" :class="getReturnClass(result.totalReturn * 100)">
                {{ formatPercent(result.totalReturn * 100) }}
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- 策略优化 -->
      <div class="card">
        <h3>🎯 策略优化</h3>
        <p class="card-description">通过网格搜索自动优化策略参数，找到最佳参数组合</p>
        
        <div class="optimization-config">
          <div class="form-group">
            <label>选择策略</label>
            <select v-model="optimization.strategyId">
              <option value="">请选择要优化的策略...</option>
              <option v-for="s in strategies" :key="s.id" :value="s.id">{{ s.name }}</option>
            </select>
          </div>
          <div class="form-group">
            <label>股票代码（用逗号分隔）</label>
            <input v-model="optimization.stockCodes" type="text" placeholder="如：000001">
          </div>
          <div class="form-group">
            <label>优化时间范围</label>
            <div class="date-range">
              <input v-model="optimization.startDate" type="date">
              <span>至</span>
              <input v-model="optimization.endDate" type="date">
            </div>
          </div>
          <div class="form-group">
            <label>优化目标</label>
            <select v-model="optimization.target">
              <option value="TotalReturn">总收益率</option>
              <option value="SharpeRatio">夏普比率</option>
              <option value="MaxDrawdown">最大回撤（最小化）</option>
              <option value="WinRate">胜率</option>
            </select>
          </div>
          <div class="form-actions">
            <button class="btn btn-success" @click="startOptimization" :disabled="optimizationLoading">🚀 开始优化</button>
            <button class="btn btn-info" @click="loadOptimizationHistory" :disabled="optimizationLoading">📊 查看历史</button>
          </div>
        </div>

        <div v-if="optimizationResult" class="optimization-results">
          <h4>优化结果</h4>
          <div class="best-result">
            <h5>最佳参数组合</h5>
            <div class="best-metrics">
              <div class="metric">
                <span class="label">总收益率:</span>
                <span class="value">{{ formatPercent(optimizationResult.totalReturn * 100) }}</span>
              </div>
              <div class="metric">
                <span class="label">夏普比率:</span>
                <span class="value">{{ optimizationResult.sharpeRatio?.toFixed(2) || '-' }}</span>
              </div>
              <div class="metric">
                <span class="label">最大回撤:</span>
                <span class="value">{{ formatPercent(optimizationResult.maxDrawdown * 100) }}</span>
              </div>
              <div class="metric">
                <span class="label">胜率:</span>
                <span class="value">{{ formatPercent(optimizationResult.winRate * 100) }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- 实时监控 -->
      <div class="card">
        <h3>📡 实时监控</h3>
        <div class="monitoring-controls">
          <button class="btn" @click="loadActiveStrategies" :disabled="loading">🔄 刷新活跃策略</button>
        </div>
        <div v-if="activeStrategies.length === 0" class="loading">暂无活跃策略</div>
        <div v-else class="active-strategies">
          <div v-for="strategy in activeStrategies" :key="strategy.id" class="strategy-item">
            <div class="strategy-info">
              <div class="strategy-name">{{ strategy.name }}</div>
              <div class="strategy-desc">{{ strategy.description || '无描述' }}</div>
            </div>
            <div class="strategy-actions">
              <button class="btn btn-small" @click="runStrategy(strategy.id)">运行</button>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, onActivated } from 'vue'
import { quantTradingService } from '../services/quantTradingService'
import { backtestService } from '../services/backtestService'
import { strategyOptimizationService } from '../services/strategyOptimizationService'
import { strategyConfigService } from '../services/strategyConfigService'

const loading = ref(false)
const backtestLoading = ref(false)
const optimizationLoading = ref(false)
const strategies = ref([])
const activeStrategies = ref([])
const showCreateStrategy = ref(false)
const editingStrategy = ref(null)

const strategyForm = ref({
  name: '',
  description: '',
  type: 'TechnicalIndicator',
  initialCapital: 100000,
  isActive: true,
  parametersJson: '{"shortPeriod": 5, "longPeriod": 20}'
})

const quickBacktest = ref({
  stockCode: '',
  startDate: new Date(Date.now() - 180 * 24 * 60 * 60 * 1000).toISOString().split('T')[0],
  endDate: new Date().toISOString().split('T')[0]
})

const quickBacktestResult = ref(null)

const batchBacktest = ref({
  stockCodes: '',
  startDate: new Date(Date.now() - 180 * 24 * 60 * 60 * 1000).toISOString().split('T')[0],
  endDate: new Date().toISOString().split('T')[0],
  initialCapital: 100000
})

const batchBacktestResults = ref([])

const optimization = ref({
  strategyId: '',
  stockCodes: '',
  startDate: new Date(Date.now() - 180 * 24 * 60 * 60 * 1000).toISOString().split('T')[0],
  endDate: new Date().toISOString().split('T')[0],
  target: 'TotalReturn'
})

const optimizationResult = ref(null)

onMounted(async () => {
  await loadStrategies()
  await loadActiveStrategies()
})

onActivated(async () => {
  await loadStrategies()
  await loadActiveStrategies()
})

const loadStrategies = async () => {
  loading.value = true
  try {
    strategies.value = await quantTradingService.getAllStrategies()
  } catch (error) {
    console.error('加载策略失败:', error)
    alert('加载策略失败: ' + (error.response?.data?.message || error.message))
  } finally {
    loading.value = false
  }
}

const loadActiveStrategies = async () => {
  try {
    activeStrategies.value = await quantTradingService.getActiveStrategies()
  } catch (error) {
    console.error('加载活跃策略失败:', error)
  }
}

const importDefaultStrategies = async () => {
  loading.value = true
  try {
    const result = await strategyConfigService.importStrategies()
    alert(result.message || `成功导入 ${result.count || result.importedCount || 0} 个策略`)
    await loadStrategies()
  } catch (error) {
    console.error('导入默认策略失败:', error)
    alert('导入失败: ' + (error.response?.data?.message || error.message))
  } finally {
    loading.value = false
  }
}

const selectStrategy = async (strategy) => {
  editingStrategy.value = strategy
  strategyForm.value = {
    name: strategy.name,
    description: strategy.description || '',
    type: strategy.type,
    initialCapital: strategy.initialCapital,
    isActive: strategy.isActive,
    parametersJson: strategy.parameters || '{}'
  }
  showCreateStrategy.value = true
}

const closeStrategyDialog = () => {
  showCreateStrategy.value = false
  editingStrategy.value = null
  strategyForm.value = {
    name: '',
    description: '',
    type: 'TechnicalIndicator',
    initialCapital: 100000,
    isActive: true,
    parametersJson: '{"shortPeriod": 5, "longPeriod": 20}'
  }
}

const saveStrategy = async () => {
  if (!strategyForm.value.name) {
    alert('请输入策略名称')
    return
  }
  
  try {
    let parameters
    try {
      parameters = JSON.parse(strategyForm.value.parametersJson)
    } catch (e) {
      alert('策略参数格式错误，请输入有效的JSON')
      return
    }

    const strategyData = {
      name: strategyForm.value.name,
      description: strategyForm.value.description,
      type: strategyForm.value.type,
      parameters: parameters,
      initialCapital: strategyForm.value.initialCapital,
      isActive: strategyForm.value.isActive
    }

    if (editingStrategy.value) {
      await quantTradingService.updateStrategy(editingStrategy.value.id, strategyData)
      alert('策略更新成功')
    } else {
      await quantTradingService.createStrategy(strategyData)
      alert('策略创建成功')
    }
    
    closeStrategyDialog()
    await loadStrategies()
  } catch (error) {
    console.error('保存策略失败:', error)
    alert('保存失败: ' + (error.response?.data?.message || error.message))
  }
}

const toggleStrategy = async (id) => {
  try {
    await quantTradingService.toggleStrategy(id)
    await loadStrategies()
    await loadActiveStrategies()
  } catch (error) {
    console.error('切换策略状态失败:', error)
    alert('操作失败: ' + (error.response?.data?.message || error.message))
  }
}

const deleteStrategy = async (id) => {
  if (!confirm('确定要删除这个策略吗？')) return
  try {
    await quantTradingService.deleteStrategy(id)
    alert('策略删除成功')
    await loadStrategies()
  } catch (error) {
    console.error('删除策略失败:', error)
    alert('删除失败: ' + (error.response?.data?.message || error.message))
  }
}

const runQuickBacktest = async () => {
  if (!quickBacktest.value.stockCode) {
    alert('请输入股票代码')
    return
  }
  if (!quickBacktest.value.startDate || !quickBacktest.value.endDate) {
    alert('请选择日期范围')
    return
  }

  backtestLoading.value = true
  try {
    quickBacktestResult.value = await backtestService.quickBacktest(
      quickBacktest.value.stockCode,
      quickBacktest.value.startDate,
      quickBacktest.value.endDate
    )
  } catch (error) {
    console.error('一键回测失败:', error)
    alert('回测失败: ' + (error.response?.data?.message || error.message))
  } finally {
    backtestLoading.value = false
  }
}

const runBatchBacktest = async () => {
  if (!batchBacktest.value.stockCodes) {
    alert('请输入股票代码')
    return
  }
  if (!batchBacktest.value.startDate || !batchBacktest.value.endDate) {
    alert('请选择日期范围')
    return
  }

  backtestLoading.value = true
  try {
    const codes = batchBacktest.value.stockCodes.split(',').map(c => c.trim()).filter(c => c)
    const results = await backtestService.quickBatchBacktest(
      codes,
      batchBacktest.value.startDate,
      batchBacktest.value.endDate,
      batchBacktest.value.initialCapital
    )
    batchBacktestResults.value = results.results || results || []
  } catch (error) {
    console.error('批量回测失败:', error)
    alert('批量回测失败: ' + (error.response?.data?.message || error.message))
  } finally {
    backtestLoading.value = false
  }
}

const selectBacktestResult = (result) => {
  quickBacktestResult.value = result
}

const startOptimization = async () => {
  if (!optimization.value.strategyId) {
    alert('请选择策略')
    return
  }
  if (!optimization.value.stockCodes) {
    alert('请输入股票代码')
    return
  }

  optimizationLoading.value = true
  try {
    const codes = optimization.value.stockCodes.split(',').map(c => c.trim()).filter(c => c)
    const result = await strategyOptimizationService.optimizeStrategy(
      optimization.value.strategyId,
      codes,
      optimization.value.startDate,
      optimization.value.endDate,
      { targetMetric: optimization.value.target }
    )
    optimizationResult.value = result
  } catch (error) {
    console.error('策略优化失败:', error)
    alert('优化失败: ' + (error.response?.data?.message || error.message))
  } finally {
    optimizationLoading.value = false
  }
}

const loadOptimizationHistory = async () => {
  if (!optimization.value.strategyId) {
    alert('请选择策略')
    return
  }
  try {
    const history = await strategyOptimizationService.getOptimizationHistory(optimization.value.strategyId)
    if (history && history.length > 0) {
      optimizationResult.value = history[0] // 显示最新的优化结果
      alert(`找到 ${history.length} 条优化历史记录`)
    } else {
      alert('暂无优化历史记录')
    }
  } catch (error) {
    console.error('加载优化历史失败:', error)
    alert('加载失败: ' + (error.response?.data?.message || error.message))
  }
}

const runStrategy = async (id) => {
  try {
    const result = await quantTradingService.runStrategy(id, null)
    alert(`策略运行完成，生成 ${result.signalCount || 0} 个交易信号`)
  } catch (error) {
    console.error('运行策略失败:', error)
    alert('运行失败: ' + (error.response?.data?.message || error.message))
  }
}

const getStrategyTypeText = (type) => {
  const map = {
    TechnicalIndicator: '技术指标',
    Fundamental: '基本面',
    Arbitrage: '套利',
    MachineLearning: '机器学习',
    Custom: '自定义'
  }
  return map[type] || type
}

const formatCurrency = (amount) => {
  return new Intl.NumberFormat('zh-CN', { style: 'currency', currency: 'CNY', minimumFractionDigits: 0 }).format(amount)
}

const formatPercent = (percent) => {
  if (percent === null || percent === undefined) return '-'
  return (percent > 0 ? '+' : '') + percent.toFixed(2) + '%'
}

const getReturnClass = (returnPercent) => {
  if (!returnPercent) return ''
  return returnPercent > 0 ? 'return-positive' : returnPercent < 0 ? 'return-negative' : ''
}
</script>

<style scoped>
.content {
  padding: 30px;
}

.strategy-controls {
  display: flex;
  gap: 10px;
  margin-bottom: 20px;
  flex-wrap: wrap;
}

.strategy-list {
  margin-top: 20px;
}

.strategy-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 15px;
  border: 1px solid #e0e0e0;
  border-radius: 8px;
  margin-bottom: 10px;
  background: #f8f9fa;
  transition: all 0.3s;
}

.strategy-item:hover {
  box-shadow: 0 2px 8px rgba(0,0,0,0.1);
  transform: translateY(-2px);
}

.strategy-info {
  flex: 1;
}

.strategy-name {
  font-size: 1.1em;
  font-weight: bold;
  color: #333;
  margin-bottom: 5px;
  display: flex;
  align-items: center;
  gap: 10px;
}

.strategy-desc {
  font-size: 0.9em;
  color: #666;
  margin-bottom: 5px;
}

.strategy-meta {
  font-size: 0.85em;
  color: #999;
  display: flex;
  gap: 15px;
}

.strategy-actions {
  display: flex;
  gap: 8px;
}

.badge {
  padding: 2px 8px;
  border-radius: 12px;
  font-size: 0.75em;
  font-weight: bold;
}

.badge-active {
  background: #28a745;
  color: white;
}

.badge-inactive {
  background: #6c757d;
  color: white;
}

.quick-backtest {
  background: #f8f9fa;
  border: 2px dashed #28a745;
  margin-bottom: 20px;
}

.quick-backtest h4 {
  color: #28a745;
  margin-bottom: 10px;
}

.quick-backtest-form {
  display: flex;
  gap: 10px;
  align-items: end;
  flex-wrap: wrap;
}

.quick-backtest-form .form-group {
  flex: 1;
  min-width: 150px;
  margin-bottom: 0;
}

.result-card {
  margin-top: 15px;
  padding: 15px;
  background: white;
  border-radius: 5px;
  border-left: 4px solid #28a745;
}

.result-message {
  font-weight: bold;
  margin-bottom: 10px;
  color: #28a745;
}

.result-stats {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: 10px;
}

.stat-item {
  display: flex;
  justify-content: space-between;
  padding: 8px;
  background: #f8f9fa;
  border-radius: 4px;
}

.stat-label {
  color: #666;
  font-size: 0.9em;
}

.stat-value {
  font-weight: bold;
}

.return-positive {
  color: #f44336;
}

.return-negative {
  color: #4caf50;
}

.backtest-config {
  display: flex;
  gap: 15px;
  margin-bottom: 15px;
}

.backtest-config .form-group {
  flex: 1;
}

.date-config {
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-width: 200px;
}

.date-config .form-group {
  margin-bottom: 0;
}

.backtest-results {
  margin-top: 20px;
}

.results-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: 10px;
  margin-top: 10px;
}

.result-item {
  border: 1px solid #ddd;
  border-radius: 5px;
  padding: 10px;
  cursor: pointer;
  transition: all 0.2s;
  background: white;
  text-align: center;
}

.result-item:hover {
  background: #f5f5f5;
  transform: translateY(-2px);
  box-shadow: 0 2px 5px rgba(0,0,0,0.1);
}

.result-stock {
  font-weight: bold;
  font-size: 1.1em;
  margin-bottom: 5px;
}

.result-return {
  font-size: 1.2em;
  font-weight: bold;
}

.optimization-config {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
  gap: 15px;
  margin-bottom: 20px;
}

.date-range {
  display: flex;
  gap: 10px;
  align-items: center;
}

.date-range span {
  color: #666;
}

.optimization-results {
  margin-top: 20px;
  padding: 20px;
  background: #e8f5e8;
  border-radius: 8px;
  border: 2px solid #28a745;
}

.best-result h5 {
  margin-bottom: 15px;
  color: #28a745;
}

.best-metrics {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 15px;
  margin-bottom: 15px;
}

.metric {
  background: white;
  padding: 10px;
  border-radius: 5px;
  display: flex;
  justify-content: space-between;
}

.metric .label {
  color: #666;
}

.metric .value {
  font-weight: bold;
  color: #28a745;
}

.active-strategies {
  margin-top: 15px;
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
  max-width: 600px;
  max-height: 90vh;
  box-shadow: 0 4px 20px rgba(0,0,0,0.3);
  display: flex;
  flex-direction: column;
}

.strategy-modal {
  max-width: 700px;
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
  overflow-y: auto;
  flex: 1;
}

.modal-footer {
  padding: 15px 25px 20px;
  border-top: 1px solid #eee;
  display: flex;
  gap: 10px;
  justify-content: flex-end;
}

.card-description {
  color: #666;
  margin-bottom: 20px;
}

@media (max-width: 768px) {
  .content {
    padding: 15px;
  }
  
  .strategy-controls {
    flex-direction: column;
  }
  
  .strategy-item {
    flex-direction: column;
    align-items: flex-start;
    gap: 10px;
  }
  
  .quick-backtest-form {
    flex-direction: column;
  }
  
  .backtest-config {
    flex-direction: column;
  }
  
  .optimization-config {
    grid-template-columns: 1fr;
  }
  
  .results-grid {
    grid-template-columns: repeat(2, 1fr);
  }
}
</style>
