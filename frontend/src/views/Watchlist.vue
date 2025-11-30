<template>
  <div class="container">
    <div class="content">
      <!-- 添加自选股表单 -->
      <div class="card">
        <h3>添加自选股</h3>
        <div class="form-group">
          <label>股票代码（如：000001）</label>
          <input v-model="form.stockCode" type="text" placeholder="输入股票代码">
        </div>
        <div class="form-group">
          <label>分类</label>
          <div style="display: flex; gap: 10px;">
            <select v-model="form.categoryId" style="flex: 1;">
              <option value="">选择分类...</option>
              <option v-for="cat in categories" :key="cat.id" :value="cat.id">
                {{ cat.name }}
              </option>
            </select>
            <button class="btn" @click="showCreateCategory = true">+ 新建分类</button>
          </div>
        </div>
        <div class="form-group">
          <div class="optional-fields-header" @click="toggleOptionalFields">
            <span>成本信息（可选）</span>
            <span class="expand-icon">{{ showOptionalFields ? '▼' : '▶' }}</span>
          </div>
          <div v-show="showOptionalFields" class="optional-fields-content">
            <div class="form-group">
              <label>成本价</label>
              <input v-model.number="form.costPrice" type="number" step="0.01" placeholder="输入成本价">
            </div>
            <div class="form-group">
              <label>持仓数量</label>
              <input v-model.number="form.quantity" type="number" placeholder="输入持仓数量">
            </div>
          </div>
        </div>
        <button class="btn" @click="handleAddStock" :disabled="loading">添加到自选股</button>
      </div>

      <!-- 创建分类对话框 -->
      <div v-if="showCreateCategory" class="modal" @click.self="showCreateCategory = false">
        <div class="modal-content">
          <div class="modal-header">
            <h3>创建新分类</h3>
            <span class="close" @click="showCreateCategory = false">&times;</span>
          </div>
          <div class="modal-body">
            <div class="form-group">
              <label>分类名称 *</label>
              <input v-model="categoryForm.name" type="text" placeholder="如：已购、预购、关注">
            </div>
            <div class="form-group">
              <label>描述</label>
              <input v-model="categoryForm.description" type="text" placeholder="分类描述（可选）">
            </div>
            <div class="form-group">
              <label>颜色</label>
              <input v-model="categoryForm.color" type="color" value="#1890ff">
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn" @click="handleCreateCategory">创建</button>
            <button class="btn btn-secondary" @click="showCreateCategory = false">取消</button>
          </div>
        </div>
      </div>

    <!-- 分类管理 -->
    <div class="card">
      <div class="card-header">
        <div class="header-title">
          <h3>分类管理</h3>
          <div class="category-summary" v-if="categories.length">
            当前分类数：<span>{{ categories.length }}</span>
          </div>
        </div>
        <div class="header-actions">
          <button class="btn btn-small" @click="openBatchModal">批量AI分析</button>
        </div>
      </div>
      <div v-if="categories.length === 0" class="loading">暂无分类，请先创建分类</div>
      <div v-else class="category-management">
        <div
          v-for="category in categories"
          :key="category.id || category.Id"
          class="category-item"
          :class="{ 'category-item--clickable': canNavigateToCategory(category) }"
          :title="canNavigateToCategory(category) ? '查看该分类下的股票' : '该分类暂无股票'"
          @click="handleCategoryClick(category)"
        >
          <div class="category-info">
            <span
              class="category-color-dot"
              :style="{ backgroundColor: category.color || category.Color || '#667eea' }"
            ></span>
            <div class="category-text">
              <div class="category-name-line">
                <span class="category-name">{{ category.name || category.Name }}</span>
                <span class="category-stocks-preview" v-if="getCategoryStockNames(category).length">
                  {{ getCategoryStockNames(category).join('、') }}
                </span>
                <span class="category-count">股票数：{{ getCategoryCount(category) }}</span>
              </div>
              <div
                class="category-description"
                v-if="category.description || category.Description"
              >
                {{ category.description || category.Description }}
              </div>
            </div>
          </div>
          <button
            class="btn btn-small btn-danger"
            @click.stop="handleDeleteCategory(category)"
            :disabled="deletingCategoryId === (category.id || category.Id)"
            title="删除分类"
          >
            {{ deletingCategoryId === (category.id || category.Id) ? '删除中...' : '删除' }}
          </button>
        </div>
      </div>
    </div>

      <!-- 批量AI分析对话框 -->
      <div v-if="batchModalVisible" class="modal" @click.self="closeBatchModal">
        <div class="modal-content batch-modal">
          <div class="modal-header">
            <h3>批量AI分析</h3>
            <span class="close" @click="closeBatchModal">&times;</span>
          </div>
          <div class="modal-body">
            <form class="batch-form" @submit.prevent="handleBatchAnalysis">
              <div class="form-group">
                <label>来源方式</label>
                <select v-model="batchForm.sourceType">
                  <option value="category">按分类（自动选择分类的股票）</option>
                  <option value="manual">手动输入股票代码</option>
                </select>
              </div>

              <div v-if="batchForm.sourceType === 'category'" class="form-group">
                <label>选择来源分类</label>
                <select v-model="batchForm.sourceCategoryId">
                  <option value="">选择分类...</option>
                  <option
                    v-for="cat in categories"
                    :key="cat.id || cat.Id"
                    :value="cat.id || cat.Id"
                  >
                    {{ cat.name || cat.Name }}
                  </option>
                </select>
              </div>

              <div v-else class="form-group">
                <label>股票代码（用逗号、空格或换行分隔）</label>
                <textarea
                  v-model="batchForm.stockCodes"
                  placeholder="例如：600519,000651,300750"
                ></textarea>
              </div>

              <div class="form-group">
                <label>目标分类（留空则自动加入「关注」分类）</label>
                <select v-model="batchForm.targetCategoryId">
                  <option value="">自动创建/使用「关注」分类</option>
                  <option
                    v-for="cat in categories"
                    :key="`target-${cat.id || cat.Id}`"
                    :value="cat.id || cat.Id"
                  >
                    {{ cat.name || cat.Name }}
                  </option>
                </select>
              </div>

              <div class="batch-form-row">
                <div class="form-group">
                  <label>分析数量（最多50只）</label>
                  <input type="number" v-model.number="batchForm.limit" min="1" max="50">
                </div>
                <div class="form-group">
                  <label>分析类型</label>
                  <select v-model="batchForm.analysisType">
                    <option v-for="item in analysisTypeOptions" :key="item.value" :value="item.value">
                      {{ item.label }}
                    </option>
                  </select>
                </div>
              </div>

              <div class="form-group checkbox">
                <label>
                  <input type="checkbox" v-model="batchForm.forceRefresh">
                  忽略缓存并重新分析
                </label>
              </div>

              <div v-if="batchError" class="error-text">{{ batchError }}</div>

              <div class="modal-footer">
                <button type="submit" class="btn" :disabled="batchLoading">
                  {{ batchLoading ? '分析中...' : '开始分析' }}
                </button>
                <button
                  type="button"
                  class="btn btn-secondary"
                  @click="closeBatchModal"
                  :disabled="batchLoading"
                >
                  取消
                </button>
              </div>
            </form>

            <div v-if="batchResults && batchResults.items && batchResults.items.length" class="batch-results">
              <h4>分析结果</h4>
              <table>
                <thead>
                  <tr>
                    <th>股票代码</th>
                    <th>股票名称</th>
                    <th>评级</th>
                    <th>操作建议</th>
                    <th>自选状态</th>
                    <th>分析状态</th>
                    <th>备注</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="item in batchResults.items" :key="`${item.stockCode}-${item.analysisTime || ''}`">
                    <td>{{ item.stockCode }}</td>
                    <td>{{ item.stockName || '-' }}</td>
                    <td>{{ item.rating || '-' }}</td>
                    <td>{{ item.actionSuggestion || '-' }}</td>
                    <td>
                      <span v-if="item.addedToWatchlist" class="status-success">已加入</span>
                      <span v-else-if="item.alreadyInWatchlist" class="status-neutral">已存在</span>
                      <span v-else class="status-muted">未加入</span>
                    </td>
                    <td>
                      <span v-if="item.analysisSucceeded" class="status-success">
                        成功 {{ item.cached ? '(缓存)' : '' }}
                      </span>
                      <span v-else class="status-failed">失败</span>
                    </td>
                    <td>{{ item.message || '' }}</td>
                  </tr>
                </tbody>
              </table>
              <p class="batch-target-tip">
                已加入分类：{{ batchResults.targetCategoryName }}（ID: {{ batchResults.targetCategoryId }}）
              </p>
            </div>
          </div>
        </div>
      </div>

      <!-- 自选股列表 -->
      <div class="card">
        <div class="card-header">
          <div>
            <h3 style="margin: 0;">我的自选股</h3>
            <p class="refresh-info">
              自动刷新: <span>{{ autoRefreshEnabled ? '已启用' : '已暂停' }}</span> | 
              间隔: <span>{{ refreshInterval }}秒</span> |
              交易状态: <span :style="{ color: isTradingTimeNow ? '#4caf50' : '#999' }">{{ tradingStatusText }}</span>
            </p>
          </div>
          <button class="btn" @click="toggleAutoRefresh">
            {{ autoRefreshEnabled ? '⏸️ 暂停' : '▶️ 开始' }}
          </button>
        </div>
        <div v-if="loading" class="loading">加载中...</div>
        <div v-else-if="stocks.length === 0" class="loading">暂无自选股</div>
        <div v-else class="stock-cards">
          <div
            v-for="(categoryStocks, categoryName) in stocksByCategory"
            :key="categoryName"
            class="category-group"
            :class="{ 'category-group--highlight': isGroupHighlighted(categoryStocks, categoryName) }"
            :ref="el => registerCategoryGroup(getCategoryGroupKeysFromStocks(categoryStocks, categoryName), el)"
          >
            <h4 class="category-title" :style="{ color: getCategoryColor(categoryName) }">
              {{ categoryName }}
            </h4>
            <div class="stock-grid">
              <div v-for="stock in categoryStocks" :key="stock.id" class="stock-card">
                <div class="stock-header">
                  <div class="stock-name-section">
                    <div class="stock-name">{{ stock.stock?.name || stock.stockName || stock.stockCode }}</div>
                    <div class="stock-code">{{ stock.stockCode }}</div>
                  </div>
                  <div class="stock-actions">
                    <select 
                      :value="stock.watchlistCategoryId || stock.category?.id || stock.Category?.id" 
                      @change="handleCategoryChange(stock.id, $event.target.value)"
                      class="category-select"
                      title="切换分类"
                    >
                      <option v-for="cat in categories" :key="cat.id" :value="cat.id">
                        {{ cat.name || cat.Name }}
                      </option>
                    </select>
                    <button class="btn btn-small btn-info" @click="handleAIAnalyze(stock)" title="AI分析">🤖 AI分析</button>
                    <button class="btn btn-small btn-danger" @click="handleRemoveStock(stock.id)">删除</button>
                  </div>
                </div>
                <div v-if="hasAiInsight(stock)" class="ai-insight">
                  <span
                    v-if="getStockRating(stock)"
                    :class="getRatingBadgeClass(getStockRating(stock))"
                  >
                    {{ getStockRating(stock) }}分
                  </span>
                  <span
                    v-if="getStockActionSuggestion(stock)"
                    class="action-chip"
                  >
                    {{ getStockActionSuggestion(stock) }}
                  </span>
                </div>
                <!-- 一键做T区域 -->
                <div class="auto-trading-section">
                  <div class="auto-trading-header">
                    <span>一键做T</span>
                    <div class="auto-trading-controls">
                      <select 
                        v-if="stock.autoTradingEnabled"
                        :value="stock.autoTradingIntervalMinutes || 30"
                        @change="handleIntervalChange(stock.id, parseInt($event.target.value))"
                        class="interval-select"
                        :disabled="togglingAutoTrading[stock.id]"
                        title="更新间隔"
                      >
                        <option :value="10">10分钟</option>
                        <option :value="30">30分钟</option>
                      </select>
                      <label class="toggle-switch">
                        <input 
                          type="checkbox" 
                          :checked="stock.autoTradingEnabled"
                          @change="handleToggleAutoTrading(stock, $event.target.checked)"
                          :disabled="togglingAutoTrading[stock.id]"
                        />
                        <span class="toggle-slider"></span>
                      </label>
                    </div>
                  </div>
                  <div v-if="stock.autoTradingEnabled && getTradingPlan(stock)" class="trading-plan-display">
                    <div class="trading-plan-item">
                      <span class="plan-label">买入:</span>
                      <span class="plan-value buy-price">{{ getTradingPlan(stock)?.buyPriceRange || '-' }}</span>
                    </div>
                    <div class="trading-plan-item">
                      <span class="plan-label">卖出:</span>
                      <span class="plan-value sell-price">{{ getTradingPlan(stock)?.sellPriceRange || '-' }}</span>
                    </div>
                    <div v-if="getTradingPlan(stock)?.suggestion" class="trading-plan-suggestion">
                      {{ getTradingPlan(stock)?.suggestion }}
                    </div>
                    <div class="trading-plan-footer">
                      <span class="plan-update-time" v-if="stock.tradingPlanUpdateTime">
                        更新: {{ getRelativeTimeText(stock.tradingPlanUpdateTime) }}
                      </span>
                      <button 
                        class="btn-refresh-plan" 
                        @click="handleRefreshTradingPlan(stock.id)"
                        :disabled="refreshingPlan[stock.id]"
                        title="手动刷新做T方案"
                      >
                        {{ refreshingPlan[stock.id] ? '刷新中...' : '🔄' }}
                      </button>
                    </div>
                  </div>
                  <div v-else-if="stock.autoTradingEnabled" class="trading-plan-loading">
                    正在生成做T方案...
                  </div>
                </div>
                <!-- 操作分析按钮区域 -->
                <div class="operation-buttons-section">
                  <div class="operation-buttons-header">
                    <span>操作分析</span>
                  </div>
                  <div class="operation-buttons">
                    <button 
                      class="btn btn-small btn-operation" 
                      @click="handleOperationAnalysisClick(stock, 'day', $event)"
                      @dblclick="handleOperationAnalysisDoubleClick(stock, 'day', $event)"
                      :disabled="analyzingOperation[stock.id] === 'day'"
                      title="基于AI分析结果，获取一日做T操作建议（双击强制刷新）"
                    >
                      {{ analyzingOperation[stock.id] === 'day' ? '分析中...' : '📈 一日做T' }}
                    </button>
                    <button 
                      class="btn btn-small btn-operation" 
                      @click="handleOperationAnalysisClick(stock, 'week', $event)"
                      @dblclick="handleOperationAnalysisDoubleClick(stock, 'week', $event)"
                      :disabled="analyzingOperation[stock.id] === 'week'"
                      title="基于AI分析结果，获取一周操作建议（双击强制刷新）"
                    >
                      {{ analyzingOperation[stock.id] === 'week' ? '分析中...' : '📊 一周操作' }}
                    </button>
                    <button 
                      class="btn btn-small btn-operation" 
                      @click="handleOperationAnalysisClick(stock, 'month', $event)"
                      @dblclick="handleOperationAnalysisDoubleClick(stock, 'month', $event)"
                      :disabled="analyzingOperation[stock.id] === 'month'"
                      title="基于AI分析结果，获取一月操作建议（双击强制刷新）"
                    >
                      {{ analyzingOperation[stock.id] === 'month' ? '分析中...' : '📅 一月操作' }}
                    </button>
                  </div>
                </div>
                <div class="price-section">
                  <div class="current-price" :class="getPriceClass(getStockChangePercent(stock))">
                    {{ formatPrice(getStockPrice(stock)) }}
                  </div>
                  <div class="price-info-row">
                    <div class="price-item">
                      <span class="price-label">涨跌幅</span>
                      <span class="price-value" :class="getPriceClass(getStockChangePercent(stock))">
                        {{ formatPercent(getStockChangePercent(stock)) }}
                      </span>
                    </div>
                    <div class="price-item">
                      <span class="price-label">涨跌额</span>
                      <span class="price-value" :class="getPriceClass(getStockChange(stock))">
                        {{ formatPrice(getStockChange(stock)) }}
                      </span>
                    </div>
                  </div>
                  <div class="price-info-row">
                    <div class="price-item">
                      <span class="price-label">最高</span>
                      <span class="price-value">{{ formatPrice(getStockHigh(stock)) }}</span>
                    </div>
                    <div class="price-item">
                      <span class="price-label">最低</span>
                      <span class="price-value">{{ formatPrice(getStockLow(stock)) }}</span>
                    </div>
                  </div>
                </div>
                <div class="cost-info-section">
                  <div class="cost-info-header">
                    <span>成本信息</span>
                    <div class="header-buttons">
                      <button 
                        v-if="stock.costPrice && !editingCost[stock.id]"
                        class="btn-icon btn-clear" 
                        @click="handleClearCost(stock.id)"
                        :disabled="savingCost[stock.id]"
                        title="清空成本信息"
                      >
                        🗑️
                      </button>
                      <button 
                        class="btn-icon" 
                        @click="toggleCostEdit(stock.id)"
                        :title="editingCost[stock.id] ? '取消编辑' : '编辑成本信息'"
                      >
                        {{ editingCost[stock.id] ? '✕' : '✎' }}
                      </button>
                    </div>
                  </div>
                  <div v-if="editingCost[stock.id]" class="cost-info-edit">
                    <div class="price-input-group">
                      <label>成本价:</label>
                      <input 
                        type="number" 
                        step="0.01" 
                        v-model.number="costForm[stock.id].costPrice"
                        placeholder="输入成本价"
                        class="price-input"
                      />
                    </div>
                    <div class="price-input-group">
                      <label>持仓数量:</label>
                      <input 
                        type="number" 
                        v-model.number="costForm[stock.id].quantity"
                        placeholder="输入持仓数量"
                        class="price-input"
                      />
                    </div>
                    <button 
                      class="btn btn-small" 
                      @click="handleSaveCost(stock.id)"
                      :disabled="savingCost[stock.id]"
                    >
                      {{ savingCost[stock.id] ? '保存中...' : '保存' }}
                    </button>
                  </div>
                  <div v-else class="cost-info" :class="stock.costPrice ? getCostClass(stock) : 'cost-neutral'">
                    <div v-if="stock.costPrice">
                      <div>成本: {{ formatPrice(stock.costPrice) }} × {{ stock.quantity || 0 }}</div>
                      <div>盈亏: {{ formatPrice(calculateProfit(stock)) }} ({{ formatPercent(calculateProfitPercent(stock)) }})</div>
                    </div>
                    <div v-else>
                      未设置成本价
                    </div>
                  </div>
                </div>
                <div class="suggested-price-section">
                  <div class="suggested-price-header">
                    <span>建议价格</span>
                    <div class="header-buttons">
                      <button 
                        v-if="(stock.suggestedBuyPrice || stock.suggestedSellPrice) && !editingSuggestedPrice[stock.id]"
                        class="btn-icon btn-clear" 
                        @click="handleClearSuggestedPrice(stock.id)"
                        :disabled="savingSuggestedPrice[stock.id]"
                        title="清空建议价格"
                      >
                        🗑️
                      </button>
                      <button 
                        class="btn-icon" 
                        @click="toggleSuggestedPriceEdit(stock.id)"
                        :title="editingSuggestedPrice[stock.id] ? '取消编辑' : '编辑建议价格'"
                      >
                        {{ editingSuggestedPrice[stock.id] ? '✕' : '✎' }}
                      </button>
                    </div>
                  </div>
                  <div v-if="editingSuggestedPrice[stock.id]" class="suggested-price-edit">
                    <div class="price-input-group">
                      <label>买入价:</label>
                      <input 
                        type="number" 
                        step="0.01" 
                        v-model.number="suggestedPriceForm[stock.id].buyPrice"
                        placeholder="建议买入价"
                        class="price-input"
                      />
                    </div>
                    <div class="price-input-group">
                      <label>卖出价:</label>
                      <input 
                        type="number" 
                        step="0.01" 
                        v-model.number="suggestedPriceForm[stock.id].sellPrice"
                        placeholder="建议卖出价"
                        class="price-input"
                      />
                    </div>
                    <button 
                      class="btn btn-small" 
                      @click="handleSaveSuggestedPrice(stock.id)"
                      :disabled="savingSuggestedPrice[stock.id]"
                    >
                      {{ savingSuggestedPrice[stock.id] ? '保存中...' : '保存' }}
                    </button>
                  </div>
                  <div v-else class="suggested-price-display">
                    <div v-if="stock.suggestedBuyPrice" class="suggested-price-item buy-price">
                      <span class="price-label">买入:</span>
                      <span class="price-value">{{ formatPrice(stock.suggestedBuyPrice) }}</span>
                      <span v-if="shouldShowBuyAlert(stock)" class="alert-badge alert-triggered">
                        <svg class="alert-icon bell-icon" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                          <path d="M12 2C8.13 2 5 5.13 5 9C5 11.38 5.97 13.54 7.5 15L6 22H18L16.5 15C18.03 13.54 19 11.38 19 9C19 5.13 15.87 2 12 2ZM12 4C14.76 4 17 6.24 17 9C17 10.65 16.32 12.13 15.24 13.11L14.75 13.5H9.25L8.76 13.11C7.68 12.13 7 10.65 7 9C7 6.24 9.24 4 12 4Z" fill="currentColor"/>
                          <path d="M9 19H15V21H9V19Z" fill="currentColor"/>
                        </svg>
                        <span class="alert-text">买入提醒</span>
                      </span>
                    </div>
                    <div v-if="stock.suggestedSellPrice" class="suggested-price-item sell-price">
                      <span class="price-label">卖出:</span>
                      <span class="price-value">{{ formatPrice(stock.suggestedSellPrice) }}</span>
                      <span v-if="shouldShowSellAlert(stock)" class="alert-badge alert-triggered">
                        <svg class="alert-icon bell-icon" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                          <path d="M12 2C8.13 2 5 5.13 5 9C5 11.38 5.97 13.54 7.5 15L6 22H18L16.5 15C18.03 13.54 19 11.38 19 9C19 5.13 15.87 2 12 2ZM12 4C14.76 4 17 6.24 17 9C17 10.65 16.32 12.13 15.24 13.11L14.75 13.5H9.25L8.76 13.11C7.68 12.13 7 10.65 7 9C7 6.24 9.24 4 12 4Z" fill="currentColor"/>
                          <path d="M9 19H15V21H9V19Z" fill="currentColor"/>
                        </svg>
                        <span class="alert-text">卖出提醒</span>
                      </span>
                    </div>
                    <div v-if="!stock.suggestedBuyPrice && !stock.suggestedSellPrice" class="no-suggested-price">
                      未设置建议价格
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- 操作分析结果对话框 -->
      <div v-if="operationAnalysisModal.visible" class="modal" @click.self="closeOperationAnalysisModal">
        <div class="modal-content operation-analysis-modal">
          <div class="modal-header">
            <h3>{{ operationAnalysisModal.stock?.stock?.name || operationAnalysisModal.stock?.stockName || operationAnalysisModal.stock?.stockCode }} - {{ operationAnalysisModal.operationTypeName }}</h3>
            <span class="close" @click="closeOperationAnalysisModal">&times;</span>
          </div>
          <div class="modal-body">
            <div v-if="operationAnalysisModal.loading" class="loading">分析中...</div>
            <div v-else-if="operationAnalysisModal.analysis" class="operation-analysis-content">
              <div v-if="operationAnalysisModal.cached && operationAnalysisModal.cacheTime" class="cache-indicator">
                <span class="cache-badge">📦 缓存结果</span>
                <span class="cache-time">缓存时间: {{ formatCacheTime(operationAnalysisModal.cacheTime) }}</span>
              </div>
              <div class="analysis-text" v-html="formatAnalysisText(operationAnalysisModal.analysis)"></div>
            </div>
            <div v-else class="loading">暂无分析结果</div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-secondary" @click="closeOperationAnalysisModal">关闭</button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, onUnmounted, onActivated, computed, watch, nextTick } from 'vue'
import { useRoute, useRouter, onBeforeRouteLeave } from 'vue-router'
import { useWatchlistStore } from '../stores/watchlist'
import { useAiAnalysisStore } from '../stores/aiAnalysis'
import api from '../services/api'
import { operationAnalysisService, watchlistService } from '../services/watchlistService'
import { isTradingTime, getTradingStatusText } from '../utils/tradingTime'

const watchlistStore = useWatchlistStore()
const aiAnalysisStore = useAiAnalysisStore()
const route = useRoute()
const router = useRouter()
const stocks = computed(() => watchlistStore.stocks)
const categories = computed(() => watchlistStore.categories)
const loading = computed(() => watchlistStore.loading)
const autoRefreshEnabled = computed({
  get: () => watchlistStore.autoRefreshEnabled,
  set: (value) => { watchlistStore.autoRefreshEnabled = value }
})
const refreshInterval = computed(() => watchlistStore.refreshInterval)
const stocksByCategory = computed(() => watchlistStore.stocksByCategory)
const stockInsightsMap = computed(() => watchlistStore.stockInsights || {})

const analysisTypeOptions = [
  { value: 'comprehensive', label: '综合分析' },
  { value: 'fundamental', label: '基本面分析' },
  { value: 'news', label: '新闻舆论分析' },
  { value: 'technical', label: '技术面分析' }
]

const batchModalVisible = ref(false)
const batchForm = ref({
  sourceType: 'category',
  stockCodes: '',
  sourceCategoryId: '',
  targetCategoryId: '',
  limit: 10,
  analysisType: 'comprehensive',
  forceRefresh: false
})
const batchResults = ref(null)
const batchLoading = ref(false)
const batchError = ref('')

const resetBatchForm = () => {
  const firstCategory = categories.value?.[0]
  const defaultCategoryId = firstCategory ? (firstCategory.id || firstCategory.Id || '') : ''
  batchForm.value = {
    sourceType: categories.value.length ? 'category' : 'manual',
    stockCodes: '',
    sourceCategoryId: defaultCategoryId ? String(defaultCategoryId) : '',
    targetCategoryId: '',
    limit: 10,
    analysisType: 'comprehensive',
    forceRefresh: false
  }
  batchResults.value = null
  batchError.value = ''
}

const openBatchModal = () => {
  resetBatchForm()
  batchModalVisible.value = true
}

const closeBatchModal = () => {
  if (batchLoading.value) return
  batchModalVisible.value = false
}

const handleBatchAnalysis = async () => {
  batchError.value = ''
  const payload = {
    analysisType: batchForm.value.analysisType,
    limit: Math.min(Math.max(Number(batchForm.value.limit) || 10, 1), 50),
    forceRefresh: batchForm.value.forceRefresh
  }

  if (batchForm.value.sourceType === 'manual') {
    const codes = (batchForm.value.stockCodes || '')
      .split(/[\s,，,;；]+/)
      .map(code => code.trim().toUpperCase())
      .filter(code => code.length > 0)

    if (codes.length === 0) {
      batchError.value = '请输入至少一个股票代码'
      return
    }

    payload.stockCodes = codes
  } else {
    const categoryId = Number(batchForm.value.sourceCategoryId)
    if (!categoryId) {
      batchError.value = '请选择来源分类'
      return
    }
    payload.watchlistCategoryId = categoryId
  }

  if (batchForm.value.targetCategoryId) {
    payload.targetCategoryId = Number(batchForm.value.targetCategoryId)
  }

  try {
    batchLoading.value = true
    const preCreatedSessions = []

    const determineDisplayName = (code) => {
      if (!code) return ''
      const stockInWatchlist = watchlistStore.stocks.find(
        s => normalizeStockCode(s.stockCode) === normalizeStockCode(code)
      )
      if (stockInWatchlist) {
        const name =
          stockInWatchlist.stock?.name ||
          stockInWatchlist.stock?.Name ||
          stockInWatchlist.stockName ||
          stockInWatchlist.stock?.nickname ||
          ''
        if (name) {
          return name
        }
      }
      const stockInResults = batchResults.value?.items?.find(
        item => normalizeStockCode(item.stockCode) === normalizeStockCode(code)
      )
      if (stockInResults?.stockName) {
        return stockInResults.stockName
      }
      return code
    }

    const prepareSession = (code) => {
      if (!code) return
      const session = aiAnalysisStore.upsertSession(code, batchForm.value.analysisType, '')
      if (session) {
        session.analyzing = true
        session.analysisType = batchForm.value.analysisType || session.analysisType || 'comprehensive'
        session.result = ''
        session.analysisTime = ''
        session.analysisDate = ''
        session.isCached = false
        session.hasAnalyzed = false
        session.lastAnalyzedStockCode = code
        session.rating = null
        session.actionSuggestion = null
        session.technicalChart = null
        const displayName = determineDisplayName(code)
        session.displayName = displayName
        session.stockInfo = {
          ...(session.stockInfo || {}),
          name: displayName || session.stockInfo?.name || ''
        }
        preCreatedSessions.push(session)
      }
    }

    if (payload.stockCodes && Array.isArray(payload.stockCodes)) {
      payload.stockCodes.forEach(code => {
        prepareSession(code)
      })
    } else if (payload.watchlistCategoryId) {
      const categoryId = Number(payload.watchlistCategoryId)
      const categoryStocks = watchlistStore.stocks.filter(stock => {
        const stockCategoryId =
          stock.watchlistCategoryId ??
          stock.category?.id ??
          stock.Category?.id ??
          null
        return stockCategoryId === categoryId
      })
      categoryStocks.forEach(stock => {
        prepareSession(stock.stockCode)
      })
    }

    batchModalVisible.value = false
    await nextTick()
    if (preCreatedSessions.length) {
      aiAnalysisStore.setActiveSession(preCreatedSessions[0].id)
    }
    router.push({ path: '/ai' })

    const handleProgressUpdate = (item) => {
      const session = aiAnalysisStore.findSessionByStockCode
        ? aiAnalysisStore.findSessionByStockCode(item.stockCode)
        : aiAnalysisStore.upsertSession(item.stockCode, batchForm.value.analysisType, item.stockName)

      if (!session) {
        return
      }

      session.analyzing = false
      session.analysisType = batchForm.value.analysisType || session.analysisType || 'comprehensive'
      session.analysisTime = item.analysisTime || session.analysisTime || ''
      session.analysisDate = item.analysisTime || session.analysisDate || ''
      session.isCached = item.cached ?? session.isCached
      session.lastAnalyzedStockCode = item.stockCode
      session.rating = item.rating ?? session.rating
      session.actionSuggestion = item.actionSuggestion ?? session.actionSuggestion
      session.technicalChart = item.technicalChart || null
      session.displayName = item.stockName || session.displayName || item.stockCode
      session.stockInfo = {
        ...(session.stockInfo || {}),
        name: item.stockName || session.stockInfo?.name || item.stockCode
      }

      if (item.analysisSucceeded) {
        session.hasAnalyzed = true
        session.result = item.analysis || session.result || ''
      } else {
        session.hasAnalyzed = false
        session.result = item.message || session.result || 'AI分析失败'
      }
    }

    const response = await watchlistStore.batchAnalyzeStocks({
      ...payload,
      onProgress: handleProgressUpdate
    })
    batchResults.value = response
    batchError.value = ''
  } catch (error) {
    const message =
      typeof error === 'string'
        ? error
        : error?.message || error?.error || '批量分析失败，请稍后重试'
    batchError.value = message
    alert(message)
  } finally {
    batchLoading.value = false
  }
}

watch(categories, (newCategories) => {
  if (!batchModalVisible.value) {
    return
  }

  if (batchForm.value.sourceType === 'category') {
    const exists = newCategories?.some(cat => (cat.id || cat.Id || '').toString() === batchForm.value.sourceCategoryId)
    if (!exists) {
      const firstCategory = newCategories?.[0]
      const defaultCategoryId = firstCategory ? (firstCategory.id || firstCategory.Id || '') : ''
      batchForm.value.sourceCategoryId = defaultCategoryId ? String(defaultCategoryId) : ''
    }
  }
})

const categoryGroupRefs = ref({})
const highlightedCategoryKey = ref(null)
let highlightTimer = null
const UNCATEGORIZED_KEY = 'uncategorized'

const getCategoryName = (category) => {
  return category?.name || category?.Name || '未分类'
}

const getRawCategoryId = (category) => {
  const id = category?.id ?? category?.Id
  return id === undefined || id === null ? null : id
}

const buildIdKey = (id) => {
  return id === null ? null : `id:${id}`
}

const buildNameKey = (name) => {
  return `name:${name ? name.toString() : UNCATEGORIZED_KEY}`
}

const normalizeStockCode = (code) => {
  if (!code) return ''
  return code.toString().trim().toUpperCase()
}

const categoryCounts = computed(() => {
  const counts = {}
  stocks.value.forEach(stock => {
    const categoryId =
      stock.watchlistCategoryId ||
      stock.category?.id ||
      stock.Category?.id ||
      null
    if (categoryId) {
      counts[categoryId] = (counts[categoryId] || 0) + 1
    }
  })
  return counts
})

const getCategoryCount = (category) => {
  const id = getRawCategoryId(category)
  if (id === null) {
    return 0
  }
  return categoryCounts.value[id] ?? categoryCounts.value[String(id)] ?? 0
}

const canNavigateToCategory = (category) => {
  return getCategoryCount(category) > 0
}

const getCategoryStockNames = (category) => {
  const id = getRawCategoryId(category)
  const targetName = getCategoryName(category)
  const names = []

  // Iterate stocks by matching category id or, if id is null, by matching category name fallback.
  stocks.value.forEach(stock => {
    const stockCategoryId =
      stock.watchlistCategoryId ??
      stock.category?.id ??
      stock.Category?.id ??
      null

    const stockCategoryName =
      stock.category?.name ??
      stock.category?.Name ??
      stock.Category?.name ??
      stock.Category?.Name ??
      '未分类'

    const isSameCategory =
      (id !== null && stockCategoryId === id) ||
      (id === null && stockCategoryId === null && stockCategoryName === targetName)

    if (isSameCategory) {
      const stockName =
        stock.stock?.name ||
        stock.stock?.Name ||
        stock.stockName ||
        stock.stockCode ||
        ''
      if (stockName) {
        names.push(stockName)
      }
    }
  })

  const maxNames = 6
  if (names.length > maxNames) {
    return [...names.slice(0, maxNames), '...']
  }
  return names
}

const extractCategoryIdFromStocks = (categoryStocks) => {
  if (!Array.isArray(categoryStocks)) {
    return null
  }
  for (const stock of categoryStocks) {
    const candidate =
      stock?.watchlistCategoryId ??
      stock?.category?.id ??
      stock?.Category?.id
    if (candidate !== undefined && candidate !== null) {
      return candidate
    }
  }
  return null
}

const getCategoryGroupKeysFromStocks = (categoryStocks, categoryName) => {
  const keys = []
  const categoryId = extractCategoryIdFromStocks(categoryStocks)
  const normalizedName = categoryName || '未分类'
  const idKey = buildIdKey(categoryId)
  if (idKey) {
    keys.push(idKey)
  }
  keys.push(buildNameKey(normalizedName))
  return Array.from(new Set(keys.filter(Boolean)))
}

const registerCategoryGroup = (keys, el) => {
  const keyList = Array.isArray(keys) ? keys : [keys]
  keyList.forEach((key) => {
    if (!key) {
      return
    }
    if (el) {
      categoryGroupRefs.value[key] = el
    } else {
      delete categoryGroupRefs.value[key]
    }
  })
}

const findCategoryGroupElement = (category) => {
  const possibleKeys = []
  const id = getRawCategoryId(category)
  if (id !== null) {
    possibleKeys.push(buildIdKey(id))
  }
  possibleKeys.push(buildNameKey(getCategoryName(category)))
  for (const key of possibleKeys) {
    if (!key) continue
    const el = categoryGroupRefs.value[key]
    if (el) {
      return el
    }
  }
  return null
}

const getPrimaryGroupKeyForCategory = (category) => {
  const id = getRawCategoryId(category)
  if (id !== null) {
    return buildIdKey(id)
  }
  return buildNameKey(getCategoryName(category))
}

const handleCategoryClick = async (category) => {
  if (!canNavigateToCategory(category)) {
    return
  }
  await nextTick()
  const targetElement = findCategoryGroupElement(category)
  if (targetElement?.scrollIntoView) {
    targetElement.scrollIntoView({ behavior: 'smooth', block: 'start' })
    const key = getPrimaryGroupKeyForCategory(category)
    highlightedCategoryKey.value = key
    if (highlightTimer) {
      clearTimeout(highlightTimer)
    }
    highlightTimer = setTimeout(() => {
      highlightedCategoryKey.value = null
    }, 1600)
  }
}

const isGroupHighlighted = (categoryStocks, categoryName) => {
  const keys = getCategoryGroupKeysFromStocks(categoryStocks, categoryName)
  return keys.some((key) => key && key === highlightedCategoryKey.value)
}

const getStockRating = (stock) => {
  if (!stock) {
    return null
  }
  if (stock.aiRating) {
    return stock.aiRating
  }
  const insight = stockInsightsMap.value[normalizeStockCode(stock.stockCode)]
  return insight?.rating || null
}

const getStockActionSuggestion = (stock) => {
  if (!stock) {
    return null
  }
  let suggestion = stock.aiActionSuggestion
  if (!suggestion) {
    const insight = stockInsightsMap.value[normalizeStockCode(stock.stockCode)]
    suggestion = insight?.actionSuggestion || null
  }
  if (!suggestion) {
    return null
  }
  return suggestion.length > 10 ? suggestion.slice(0, 10) : suggestion
}

const hasAiInsight = (stock) => {
  return !!(getStockRating(stock) || getStockActionSuggestion(stock))
}

const getRatingBadgeClass = (rating) => {
  // 将评级转换为数字（支持字符串和数字类型）
  const score = typeof rating === 'string' ? parseInt(rating, 10) : rating
  
  // 如果不是有效数字，返回默认样式
  if (isNaN(score)) {
    return 'rating-badge neutral'
  }
  
  // 根据分数范围返回对应的CSS类
  if (score >= 80) {
    return 'rating-badge excellence'
  } else if (score >= 60) {
    return 'rating-badge good'
  } else if (score >= 40) {
    return 'rating-badge neutral'
  } else {
    return 'rating-badge risk'
  }
}

const form = ref({
  stockCode: '',
  categoryId: '',
  costPrice: null,
  quantity: null
})

const categoryForm = ref({
  name: '',
  description: '',
  color: '#1890ff'
})

const showCreateCategory = ref(false)
let refreshTimer = null
let tradingStatusTimer = null
const deletingCategoryId = ref(null)

// 交易状态相关
const isTradingTimeNow = ref(isTradingTime())
const tradingStatusText = ref(getTradingStatusText())

// 建议价格编辑相关
const editingSuggestedPrice = ref({})
const suggestedPriceForm = ref({})
const savingSuggestedPrice = ref({})

// 成本信息编辑相关
const editingCost = ref({})
const costForm = ref({})
const savingCost = ref({})

// 表单可选字段折叠控制
const showOptionalFields = ref(false)

// 操作分析相关
const analyzingOperation = ref({})
const operationAnalysisModal = ref({
  visible: false,
  stock: null,
  operationType: '',
  operationTypeName: '',
  analysis: '',
  loading: false,
  cached: false,
  cacheTime: null
})
const lastClickTime = ref({}) // 用于检测双击
const clickTimer = ref({}) // 用于延迟处理单击

// 一键做T相关
const togglingAutoTrading = ref({})
const refreshingPlan = ref({})
const currentTime = ref(new Date()) // 用于实时更新相对时间显示

// 定时器，每秒更新一次当前时间，用于实时刷新相对时间显示
let timeUpdateTimer = null

// 启动时间更新定时器
const startTimeUpdateTimer = () => {
  if (timeUpdateTimer) return
  timeUpdateTimer = setInterval(() => {
    currentTime.value = new Date()
  }, 1000) // 每秒更新一次
}

// 停止时间更新定时器
const stopTimeUpdateTimer = () => {
  if (timeUpdateTimer) {
    clearInterval(timeUpdateTimer)
    timeUpdateTimer = null
  }
}

// 组件挂载时加载数据
onMounted(async () => {
  // 从localStorage加载设置
  loadSettings()
  await watchlistStore.fetchWatchlist()
  await watchlistStore.fetchCategories()
  startAutoRefresh()
  
  // 启动时间更新定时器，用于实时刷新相对时间显示
  startTimeUpdateTimer()
  
  // 监听store中的refreshInterval变化，重新创建定时器
  watch(() => watchlistStore.refreshInterval, (newInterval) => {
    if (autoRefreshEnabled.value) {
      startAutoRefresh()
    }
  })
  
  // 监听store中的autoRefreshEnabled变化
  watch(() => watchlistStore.autoRefreshEnabled, (enabled) => {
    if (enabled) {
      startAutoRefresh()
    } else {
      stopAutoRefresh()
    }
  })
})

// 组件离开时保存滚动位置
onBeforeRouteLeave(() => {
  const scrollTop = window.pageYOffset || document.documentElement.scrollTop
  localStorage.setItem('watchlist-scroll-position', scrollTop.toString())
})

// 组件激活时恢复自动刷新和滚动位置（用于路由切换回来时，keep-alive 会触发此钩子）
onActivated(() => {
  // 重新加载设置，确保使用最新的刷新间隔
  loadSettings()
  // 更新交易状态
  updateTradingStatus()
  // 只恢复自动刷新，不重新获取数据
  startAutoRefresh()
  // 启动时间更新定时器
  startTimeUpdateTimer()
  // 重新连接SSE（如果未连接）
  if (!sseConnection || sseConnection.readyState === EventSource.CLOSED) {
    connectTradingPlanSSE()
  }

  // 恢复滚动位置
  nextTick(() => {
    const savedScrollTop = localStorage.getItem('watchlist-scroll-position')
    if (savedScrollTop) {
      window.scrollTo({
        top: parseInt(savedScrollTop, 10),
        behavior: 'instant'
      })
    }
  })
})

onUnmounted(() => {
  stopAutoRefresh()
  stopTimeUpdateTimer() // 停止时间更新定时器
  if (highlightTimer) {
    clearTimeout(highlightTimer)
    highlightTimer = null
  }
  // 确保恢复背景页面滚动
  enableBodyScroll()
})

// 监听模态框状态，确保关闭时恢复滚动
watch(() => operationAnalysisModal.value.visible, (visible) => {
  if (!visible) {
    enableBodyScroll()
  }
})

// 加载设置
const loadSettings = () => {
  const savedInterval = localStorage.getItem('refreshInterval')
  const savedEnabled = localStorage.getItem('autoRefreshEnabled')
  
  if (savedInterval) {
    const interval = parseFloat(savedInterval)
    // 直接更新 store 中的 ref，避免写入 computed 属性
    watchlistStore.$patch({ refreshInterval: interval })
    refreshInterval.value = interval
  } else {
    refreshInterval.value = watchlistStore.refreshInterval
  }
  
  if (savedEnabled !== null) {
    const enabled = savedEnabled === 'true'
    watchlistStore.autoRefreshEnabled = enabled
    autoRefreshEnabled.value = enabled
  } else {
    autoRefreshEnabled.value = watchlistStore.autoRefreshEnabled
  }
}

// SSE连接（用于接收做T方案更新推送）
let sseConnection = null

const startAutoRefresh = () => {
  // 先清除现有定时器，避免重复创建
  if (refreshTimer) {
    clearInterval(refreshTimer)
    refreshTimer = null
  }
  
  if (autoRefreshEnabled.value) {
    const intervalSeconds = refreshInterval.value || watchlistStore.refreshInterval || 3
    console.log('启动自动刷新，间隔:', intervalSeconds, '秒')
    refreshTimer = setInterval(() => {
      // 只有在有股票且在交易时间内时才刷新
      if (watchlistStore.stocks.length > 0 && isTradingTime()) {
        watchlistStore.refreshPrices()
      }
    }, intervalSeconds * 1000)
  }
  
  // 启动交易状态更新定时器（每分钟更新一次）
  if (!tradingStatusTimer) {
    updateTradingStatus()
    tradingStatusTimer = setInterval(() => {
      updateTradingStatus()
    }, 60000) // 每分钟更新一次
  }
  
  // 连接SSE以接收做T方案更新推送（替代定时刷新）
  if (!sseConnection || sseConnection.readyState === EventSource.CLOSED) {
    connectTradingPlanSSE()
  }
}

// 连接SSE以接收做T方案更新推送
const connectTradingPlanSSE = () => {
  // 如果已有连接，先关闭
  if (sseConnection) {
    sseConnection.close()
    sseConnection = null
  }

  try {
    // 连接到SSE端点
    const eventSource = new EventSource('/api/watchlist/trading-plan/events')
    sseConnection = eventSource

    eventSource.onopen = () => {
      console.log('✅ 做T方案更新推送连接已建立')
    }

    eventSource.onmessage = async (event) => {
      try {
        const data = JSON.parse(event.data)
        
        if (data.type === 'tradingPlanUpdated') {
          // 接收到做T方案更新事件，获取该股票的完整数据并更新
          const watchlistStockId = data.watchlistStockId
          const updateTime = new Date(data.updateTime)
          
          console.log(`📢 收到做T方案更新推送: 股票ID=${watchlistStockId}, 更新时间=${updateTime.toLocaleString()}`)
          
          // 获取该股票的完整数据
          const allStocks = await watchlistService.getWatchlist()
          const updatedStocksArray = Array.isArray(allStocks) ? allStocks : (allStocks?.data || [])
          const updatedStock = updatedStocksArray.find(s => s.id === watchlistStockId)
          
          if (updatedStock) {
            // 使用 updateSingleStock 函数更新，确保触发 Vue 响应式更新
            updateSingleStock(updatedStock)
            
            // 强制更新 currentTime 以触发时间文本重新计算
            // 使用 nextTick 确保 DOM 更新完成后再更新时间
            await nextTick()
            currentTime.value = new Date()
            
            console.log(`✅ 已更新股票 ${updatedStock.stockCode} 的做T方案数据，更新时间: ${updatedStock.tradingPlanUpdateTime}`)
          }
        } else if (data.type === 'connected') {
          console.log('✅ SSE连接已确认:', data.clientId)
        } else if (data.type === 'heartbeat') {
          // 心跳包，不做处理
        }
      } catch (error) {
        console.error('处理SSE消息失败:', error)
      }
    }

    eventSource.onerror = (error) => {
      console.error('❌ SSE连接错误:', error)
      // 连接断开后，5秒后尝试重连
      setTimeout(() => {
        if (!sseConnection || sseConnection.readyState === EventSource.CLOSED) {
          console.log('🔄 尝试重新连接SSE...')
          connectTradingPlanSSE()
        }
      }, 5000)
    }
  } catch (error) {
    console.error('❌ 创建SSE连接失败:', error)
    // 如果浏览器不支持EventSource，降级到定时刷新
    console.warn('⚠️ 浏览器不支持SSE，将使用定时刷新作为降级方案')
  }
}

const updateTradingStatus = () => {
  isTradingTimeNow.value = isTradingTime()
  tradingStatusText.value = getTradingStatusText()
}

const stopAutoRefresh = () => {
  if (refreshTimer) {
    clearInterval(refreshTimer)
    refreshTimer = null
  }
  if (tradingStatusTimer) {
    clearInterval(tradingStatusTimer)
    tradingStatusTimer = null
  }
  // 关闭SSE连接
  if (sseConnection) {
    sseConnection.close()
    sseConnection = null
  }
}

const toggleAutoRefresh = () => {
  autoRefreshEnabled.value = !autoRefreshEnabled.value
  watchlistStore.autoRefreshEnabled = autoRefreshEnabled.value
  localStorage.setItem('autoRefreshEnabled', autoRefreshEnabled.value.toString())
  if (autoRefreshEnabled.value) {
    startAutoRefresh()
  } else {
    stopAutoRefresh()
  }
}

const handleAddStock = async () => {
  if (!form.value.stockCode) {
    alert('请输入股票代码')
    return
  }
  try {
    await watchlistStore.addStock(
      form.value.stockCode,
      form.value.categoryId || null,
      form.value.costPrice || null,
      form.value.quantity || null
    )
    form.value = { stockCode: '', categoryId: '', costPrice: null, quantity: null }
  } catch (error) {
    // 提取友好的错误消息
    let errorMessage = '添加失败，请稍后重试'
    
    if (error.response) {
      const responseData = error.response.data
      
      // 后端返回的字符串错误消息（如："该股票已存在于此分类"）
      if (typeof responseData === 'string' && responseData.trim()) {
        errorMessage = responseData
      } 
      // JSON格式的错误响应
      else if (responseData && typeof responseData === 'object') {
        errorMessage = responseData.message || responseData.error || errorMessage
      }
    } else if (error.message && !error.message.includes('status code')) {
      // 如果不是技术性错误消息，使用原始消息
      errorMessage = error.message
    }
    
    // 显示友好的错误提示
    alert(errorMessage)
  }
}

const handleRemoveStock = async (id) => {
  if (!confirm('确定要删除这只股票吗？')) return
  try {
    await watchlistStore.removeStock(id)
  } catch (error) {
    alert('删除失败: ' + (error.response?.data?.message || error.message))
  }
}

const handleCreateCategory = async () => {
  if (!categoryForm.value.name) {
    alert('请输入分类名称')
    return
  }
  try {
    await watchlistStore.createCategory(
      categoryForm.value.name,
      categoryForm.value.description,
      categoryForm.value.color
    )
    categoryForm.value = { name: '', description: '', color: '#1890ff' }
    showCreateCategory.value = false
  } catch (error) {
    alert('创建失败: ' + (error.response?.data?.message || error.message))
  }
}

const handleDeleteCategory = async (category) => {
  const id = category?.id || category?.Id
  if (!id) {
    return
  }
  if (categories.value.length <= 1) {
    alert('至少需要保留一个分类，无法删除。')
    return
  }

  const count = categoryCounts.value[id] || 0
  const name = category?.name || category?.Name || ''
  const displayName = name || `ID ${id}`
  const message =
    count > 0
      ? `分类「${displayName}」下仍有 ${count} 只股票，删除后这些股票将移动到“未分类”。确定继续删除吗？`
      : `确定要删除分类「${displayName}」吗？`

  if (!confirm(message)) {
    return
  }

  try {
    deletingCategoryId.value = id
    await watchlistStore.deleteCategory(id)
    await watchlistStore.fetchCategories()
    await watchlistStore.fetchWatchlist()
  } catch (error) {
    const errorMessage =
      error?.response?.data?.message ||
      error?.response?.data?.error ||
      error?.message ||
      '删除分类失败，请稍后重试'
    alert(errorMessage)
  } finally {
    deletingCategoryId.value = null
  }
}

const handleCategoryChange = async (stockId, categoryId) => {
  try {
    await watchlistStore.updateCategory(stockId, parseInt(categoryId))
  } catch (error) {
    alert('更新分类失败: ' + (error.response?.data?.message || error.message))
    // 如果失败，重新加载数据以恢复原状态
    await watchlistStore.fetchWatchlist()
  }
}

const toggleSuggestedPriceEdit = (stockId) => {
  if (editingSuggestedPrice.value[stockId]) {
    // 取消编辑
    delete editingSuggestedPrice.value[stockId]
    delete suggestedPriceForm.value[stockId]
  } else {
    // 开始编辑
    const stock = stocks.value.find(s => s.id === stockId)
    editingSuggestedPrice.value[stockId] = true
    suggestedPriceForm.value[stockId] = {
      buyPrice: stock?.suggestedBuyPrice || null,
      sellPrice: stock?.suggestedSellPrice || null
    }
  }
}

const handleSaveSuggestedPrice = async (stockId) => {
  try {
    savingSuggestedPrice.value[stockId] = true
    const form = suggestedPriceForm.value[stockId]
    await watchlistStore.updateSuggestedPrice(
      stockId,
      form.buyPrice || null,
      form.sellPrice || null
    )
    // 立即关闭编辑模式，不等待列表刷新
    delete editingSuggestedPrice.value[stockId]
    delete suggestedPriceForm.value[stockId]
  } catch (error) {
    alert('保存建议价格失败: ' + (error.response?.data?.message || error.message))
  } finally {
    delete savingSuggestedPrice.value[stockId]
  }
}

const handleClearSuggestedPrice = async (stockId) => {
  if (!confirm('确定要清空建议价格吗？')) return
  
  try {
    savingSuggestedPrice.value[stockId] = true
    await watchlistStore.updateSuggestedPrice(stockId, null, null)
  } catch (error) {
    alert('清空建议价格失败: ' + (error.response?.data?.message || error.message))
  } finally {
    delete savingSuggestedPrice.value[stockId]
  }
}

const toggleCostEdit = (stockId) => {
  if (editingCost.value[stockId]) {
    // 取消编辑
    delete editingCost.value[stockId]
    delete costForm.value[stockId]
  } else {
    // 开始编辑
    const stock = stocks.value.find(s => s.id === stockId)
    editingCost.value[stockId] = true
    costForm.value[stockId] = {
      costPrice: stock?.costPrice || null,
      quantity: stock?.quantity || null
    }
  }
}

const toggleOptionalFields = () => {
  showOptionalFields.value = !showOptionalFields.value
}

const handleSaveCost = async (stockId) => {
  try {
    savingCost.value[stockId] = true
    const form = costForm.value[stockId]
    await watchlistStore.updateStock(
      stockId,
      form.costPrice || null,
      form.quantity || null
    )
    // 立即关闭编辑模式，不等待列表刷新
    delete editingCost.value[stockId]
    delete costForm.value[stockId]
  } catch (error) {
    alert('保存成本信息失败: ' + (error.response?.data?.message || error.message))
  } finally {
    delete savingCost.value[stockId]
  }
}

const handleClearCost = async (stockId) => {
  if (!confirm('确定要清空成本信息吗？')) return
  
  try {
    savingCost.value[stockId] = true
    await watchlistStore.updateStock(stockId, null, null)
  } catch (error) {
    alert('清空成本信息失败: ' + (error.response?.data?.message || error.message))
  } finally {
    delete savingCost.value[stockId]
  }
}

const getCategoryColor = (categoryName) => {
  const category = categories.value.find(c => (c.name || c.Name) === categoryName)
  return category?.color || category?.Color || '#667eea'
}

const getPriceClass = (value) => {
  if (!value) return ''
  return value > 0 ? 'price-up' : value < 0 ? 'price-down' : ''
}

const getCostClass = (stock) => {
  const profit = calculateProfit(stock)
  return profit >= 0 ? 'cost-positive' : 'cost-negative'
}

const calculateProfit = (stock) => {
  const currentPrice = getStockPrice(stock)
  if (!stock.costPrice || !stock.quantity || !currentPrice) return 0
  return (currentPrice - stock.costPrice) * stock.quantity
}

const calculateProfitPercent = (stock) => {
  const currentPrice = getStockPrice(stock)
  if (!stock.costPrice || !currentPrice) return 0
  return ((currentPrice - stock.costPrice) / stock.costPrice) * 100
}

const formatPrice = (price) => {
  if (price === null || price === undefined) return '-'
  return price.toFixed(2)
}

const formatPercent = (percent) => {
  if (percent === null || percent === undefined) return '-'
  return (percent > 0 ? '+' : '') + percent.toFixed(2) + '%'
}

// AI分析
const handleAIAnalyze = (stockItem) => {
  if (!stockItem) {
    return
  }
  const code = typeof stockItem === 'string' ? stockItem : stockItem.stockCode
  if (!code) {
    return
  }
  const name =
    typeof stockItem === 'object'
      ? stockItem.stock?.name || stockItem.stockName || stockItem.stock?.Name || ''
      : ''
  aiAnalysisStore.upsertSession(code, undefined, name)
  const query = { stockCode: code }
  if (name) {
    query.stockName = name
  }
  router.push({ path: '/ai', query })
}

// 获取股票价格相关的辅助函数
const getStockPrice = (stock) => {
  return stock.stock?.currentPrice || stock.stock?.price || stock.currentPrice || 0
}

const getStockChange = (stock) => {
  return stock.stock?.change || stock.change || 0
}

const getStockChangePercent = (stock) => {
  return stock.stock?.changePercent || stock.changePercent || 0
}

const getStockHigh = (stock) => {
  // 优先使用 highPrice（后端返回的 JSON 字段名），然后尝试其他可能的字段名
  const high = stock.stock?.highPrice || stock.stock?.high || stock.highPrice || stock.high || 0
  // 如果最高价为0，使用当前价作为回退（非交易时间可能为0）
  const currentPrice = getStockPrice(stock)
  if (high > 0) {
    return high
  }
  // 如果最高价为0但有当前价，使用当前价
  if (currentPrice > 0) {
    return currentPrice
  }
  return 0
}

const getStockLow = (stock) => {
  // 优先使用 lowPrice（后端返回的 JSON 字段名），然后尝试其他可能的字段名
  const low = stock.stock?.lowPrice || stock.stock?.low || stock.lowPrice || stock.low || 0
  // 如果最低价为0，使用当前价作为回退（非交易时间可能为0）
  const currentPrice = getStockPrice(stock)
  if (low > 0) {
    return low
  }
  // 如果最低价为0但有当前价，使用当前价
  if (currentPrice > 0) {
    return currentPrice
  }
  return 0
}

// 判断是否应该显示买入提醒
// 只用当前价跟买入价做对比：如果当前价 < 买入价，则提醒
const shouldShowBuyAlert = (stock) => {
  if (!stock.suggestedBuyPrice) return false
  
  const currentPrice = getStockPrice(stock)
  const buyPrice = stock.suggestedBuyPrice
  
  // 如果当前价格无效，不显示提醒
  if (!currentPrice || currentPrice <= 0) {
    return false
  }
  
  // 当前价格 < 买入价时，显示买入提醒
  return currentPrice < buyPrice
}

// 判断是否应该显示卖出提醒
// 只用当前价跟卖出价做对比：如果当前价 > 卖出价，则提醒
const shouldShowSellAlert = (stock) => {
  if (!stock.suggestedSellPrice) return false
  
  const currentPrice = getStockPrice(stock)
  const sellPrice = stock.suggestedSellPrice
  
  // 如果当前价格无效，不显示提醒
  if (!currentPrice || currentPrice <= 0) {
    return false
  }
  
  // 当前价格 > 卖出价时，显示卖出提醒
  return currentPrice > sellPrice
}

// 处理单击操作分析按钮
const handleOperationAnalysisClick = (stock, operationType, event) => {
  event.preventDefault()
  const key = `${stock.id}_${operationType}`
  const now = Date.now()
  const lastClick = lastClickTime.value[key] || 0
  const timeDiff = now - lastClick

  // 如果两次点击间隔小于300ms，认为是双击，不处理单击
  if (timeDiff < 300 && timeDiff > 0) {
    return
  }

  lastClickTime.value[key] = now

  // 延迟处理单击，等待可能的双击
  if (clickTimer.value[key]) {
    clearTimeout(clickTimer.value[key])
  }

  clickTimer.value[key] = setTimeout(() => {
    handleOperationAnalysis(stock, operationType, false)
  }, 300)
}

// 处理双击操作分析按钮（强制刷新）
const handleOperationAnalysisDoubleClick = (stock, operationType, event) => {
  event.preventDefault()
  const key = `${stock.id}_${operationType}`
  
  // 清除单击的延迟处理
  if (clickTimer.value[key]) {
    clearTimeout(clickTimer.value[key])
    delete clickTimer.value[key]
  }
  
  // 立即执行强制刷新
  handleOperationAnalysis(stock, operationType, true)
}

// 操作分析处理
const handleOperationAnalysis = async (stock, operationType, forceRefresh = false) => {
  if (!stock || !stock.stockCode) {
    alert('股票信息不完整')
    return
  }

  const stockId = stock.id
  analyzingOperation.value[stockId] = operationType

  try {
    const response = await operationAnalysisService.getOperationAnalysis(
      stock.stockCode,
      operationType,
      forceRefresh
    )

    if (response && response.success) {
      const operationTypeName = operationType === 'day' ? '一日做T' 
        : operationType === 'week' ? '一周操作' 
        : '一月操作'
      
      operationAnalysisModal.value = {
        visible: true,
        stock: stock,
        operationType: operationType,
        operationTypeName: operationTypeName,
        analysis: response.analysis || '',
        loading: false,
        cached: response.cached || false,
        cacheTime: response.cacheTime ? new Date(response.cacheTime) : null
      }
      
      // 禁止背景页面滚动
      disableBodyScroll()
    } else {
      alert(response?.message || '获取操作分析失败')
    }
  } catch (error) {
    console.error('操作分析失败:', error)
    
    // 检查是否是缺少AI分析的错误
    if (error.response?.status === 400) {
      const errorData = error.response.data
      if (errorData?.error === 'NO_AI_ANALYSIS' || errorData?.requiresAnalysis) {
        const stockName = stock.stock?.name || stock.stockName || stock.stockCode
        if (confirm(`该股票（${stockName}）尚未进行AI分析。\n\n是否现在进行AI分析？`)) {
          handleAIAnalyze(stock)
        }
      } else {
        alert(errorData?.message || '获取操作分析失败，请稍后重试')
      }
    } else {
      alert(error?.message || '获取操作分析失败，请稍后重试')
    }
  } finally {
    analyzingOperation.value[stockId] = null
  }
}

const closeOperationAnalysisModal = () => {
  operationAnalysisModal.value = {
    visible: false,
    stock: null,
    operationType: '',
    operationTypeName: '',
    analysis: '',
    loading: false,
    cached: false,
    cacheTime: null
  }
  
  // 恢复背景页面滚动
  enableBodyScroll()
}

// 禁止背景页面滚动
const disableBodyScroll = () => {
  const scrollY = window.scrollY
  document.body.style.position = 'fixed'
  document.body.style.top = `-${scrollY}px`
  document.body.style.width = '100%'
  document.body.style.overflow = 'hidden'
}

// 恢复背景页面滚动
const enableBodyScroll = () => {
  const scrollY = document.body.style.top
  document.body.style.position = ''
  document.body.style.top = ''
  document.body.style.width = ''
  document.body.style.overflow = ''
  if (scrollY) {
    window.scrollTo(0, parseInt(scrollY || '0') * -1)
  }
}

// 格式化缓存时间
const formatCacheTime = (cacheTime) => {
  if (!cacheTime) return ''
  const now = new Date()
  const diff = now - cacheTime
  const minutes = Math.floor(diff / 60000)
  const hours = Math.floor(minutes / 60)
  const days = Math.floor(hours / 24)
  
  if (days > 0) {
    return `${days}天前`
  } else if (hours > 0) {
    return `${hours}小时前`
  } else if (minutes > 0) {
    return `${minutes}分钟前`
  } else {
    return '刚刚'
  }
}

// 格式化相对时间（使用响应式当前时间，实时更新）
const getRelativeTimeText = (time) => {
  if (!time) return ''
  // 使用响应式的当前时间，这样当 currentTime 更新时，显示也会自动更新
  const now = currentTime.value
  const date = typeof time === 'string' ? new Date(time) : time
  const diff = now - date
  const minutes = Math.floor(diff / 60000)
  const hours = Math.floor(minutes / 60)
  const days = Math.floor(hours / 24)
  
  if (days > 0) {
    return `${days}天前`
  } else if (hours > 0) {
    return `${hours}小时前`
  } else if (minutes > 0) {
    return `${minutes}分钟前`
  } else {
    return '刚刚'
  }
}

// 格式化相对时间（保持向后兼容）
const formatRelativeTime = (time) => {
  return getRelativeTimeText(time)
}

// 获取做T方案
const getTradingPlan = (stock) => {
  if (!stock.tradingPlan) return null
  try {
    return typeof stock.tradingPlan === 'string' 
      ? JSON.parse(stock.tradingPlan) 
      : stock.tradingPlan
  } catch {
    return null
  }
}

// 更新单个股票的数据（只更新当前卡片，不刷新整个列表）
const updateSingleStock = (updatedStock) => {
  const index = stocks.value.findIndex(s => s.id === updatedStock.id)
  if (index !== -1) {
    // 保留原有的stock对象（包含实时价格数据），只更新做T相关字段
    const existingStock = stocks.value[index]
    // 更新做T相关字段
    existingStock.autoTradingEnabled = updatedStock.autoTradingEnabled
    existingStock.autoTradingIntervalMinutes = updatedStock.autoTradingIntervalMinutes
    existingStock.tradingPlan = updatedStock.tradingPlan
    existingStock.tradingPlanUpdateTime = updatedStock.tradingPlanUpdateTime
    existingStock.lastUpdate = updatedStock.lastUpdate
  }
}

// 切换一键做T
const handleToggleAutoTrading = async (stock, enabled) => {
  const stockId = stock.id
  togglingAutoTrading.value[stockId] = true
  
  try {
    // 使用股票当前的间隔设置，如果没有则默认30分钟
    const intervalMinutes = stock.autoTradingIntervalMinutes || 30
    const updatedStock = await watchlistService.toggleAutoTrading(stockId, enabled, intervalMinutes)
    // 只更新当前股票的数据，不刷新整个列表
    if (updatedStock) {
      updateSingleStock(updatedStock)
      // 强制更新 currentTime 以触发时间显示重新计算
      await nextTick()
      currentTime.value = new Date()
    }
  } catch (error) {
    console.error('切换做T状态失败:', error)
    alert(error?.response?.data?.message || '切换做T状态失败，请稍后重试')
  } finally {
    togglingAutoTrading.value[stockId] = false
  }
}

// 修改更新间隔
const handleIntervalChange = async (stockId, intervalMinutes) => {
  try {
    // 获取当前股票信息
    const stock = stocks.value.find(s => s.id === stockId)
    if (!stock || !stock.autoTradingEnabled) return
    
    togglingAutoTrading.value[stockId] = true
    const updatedStock = await watchlistService.toggleAutoTrading(stockId, true, intervalMinutes)
    // 只更新当前股票的数据，不刷新整个列表
    if (updatedStock) {
      updateSingleStock(updatedStock)
      // 强制更新 currentTime 以触发时间显示重新计算
      await nextTick()
      currentTime.value = new Date()
    }
  } catch (error) {
    console.error('修改更新间隔失败:', error)
    alert(error?.response?.data?.message || '修改更新间隔失败，请稍后重试')
  } finally {
    togglingAutoTrading.value[stockId] = false
  }
}

// 手动刷新做T方案
const handleRefreshTradingPlan = async (stockId) => {
  refreshingPlan.value[stockId] = true
  
  try {
    const updatedStock = await watchlistService.refreshTradingPlan(stockId)
    // 只更新当前股票的数据，不刷新整个列表
    if (updatedStock) {
      updateSingleStock(updatedStock)
    }
  } catch (error) {
    console.error('刷新做T方案失败:', error)
    alert(error?.response?.data?.message || '刷新做T方案失败，请稍后重试')
  } finally {
    refreshingPlan.value[stockId] = false
  }
}

// 格式化分析文本（将换行转换为HTML）
const formatAnalysisText = (text) => {
  if (!text) return ''
  
  // 转义HTML特殊字符，防止XSS攻击
  const escapeHtml = (str) => {
    const div = document.createElement('div')
    div.textContent = str
    return div.innerHTML
  }
  
  let formatted = escapeHtml(text)
  
  // 处理标题（## 标题 或 ### 标题）
  formatted = formatted.replace(/^###\s+(.+?)$/gm, '<h4>$1</h4>')
  formatted = formatted.replace(/^##\s+(.+?)$/gm, '<h3>$1</h3>')
  
  // 处理粗体（**文本**）
  formatted = formatted.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
  
  // 处理列表项（- 或 • 开头）
  const lines = formatted.split('\n')
  let inList = false
  let result = []
  
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i]
    const isListItem = /^[-•]\s+(.+)$/.test(line)
    
    if (isListItem) {
      if (!inList) {
        result.push('<ul>')
        inList = true
      }
      result.push(line.replace(/^[-•]\s+(.+)$/, '<li>$1</li>'))
    } else {
      if (inList) {
        result.push('</ul>')
        inList = false
      }
      if (line.trim()) {
        result.push(`<p>${line}</p>`)
      } else {
        result.push('<br>')
      }
    }
  }
  
  if (inList) {
    result.push('</ul>')
  }
  
  return result.join('')
}
</script>

<style scoped>
.content {
  padding: 30px;
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.header-title {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.header-title h3 {
  margin: 0;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.stock-cards {
  margin-top: 20px;
}

.category-group {
  margin-bottom: 30px;
  scroll-margin-top: 80px;
}

.category-title {
  font-size: 1.2em;
  font-weight: bold;
  margin-bottom: 15px;
  padding-bottom: 8px;
  border-bottom: 2px solid #f0f0f0;
}

.stock-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(350px, 1fr));
  gap: 20px;
}

.stock-card {
  background: white;
  border: 1px solid #e0e0e0;
  border-radius: 8px;
  padding: 20px;
  position: relative;
  transition: all 0.3s;
  box-shadow: 0 2px 5px rgba(0,0,0,0.1);
}

.stock-card:hover {
  box-shadow: 0 4px 10px rgba(0,0,0,0.15);
  transform: translateY(-2px);
}

.category-group--highlight .category-title {
  color: #1890ff;
}

.category-group--highlight .stock-card {
  border-color: rgba(24, 144, 255, 0.45);
  box-shadow: 0 0 0 2px rgba(24, 144, 255, 0.15);
}

.stock-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 15px;
  padding-bottom: 10px;
  border-bottom: 2px solid #f0f0f0;
}

.stock-name {
  font-size: 1.5em;
  font-weight: bold;
  color: #333;
  margin-bottom: 5px;
}

.stock-code {
  font-size: 0.9em;
  color: #666;
}

.stock-actions {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.btn-small {
  padding: 6px 12px;
  font-size: 0.85em;
}

.btn-info {
  background: #17a2b8;
}

.btn-info:hover {
  background: #138496;
}

.category-select {
  padding: 6px 12px;
  font-size: 0.85em;
  border: 1px solid #ddd;
  border-radius: 4px;
  background: white;
  cursor: pointer;
  min-width: 100px;
  transition: all 0.3s;
}

.category-select:hover {
  border-color: #1890ff;
}

.category-select:focus {
  outline: none;
  border-color: #1890ff;
  box-shadow: 0 0 0 2px rgba(24, 144, 255, 0.2);
}

.category-management {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-top: 10px;
}

.category-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
  border: 1px solid #e0e0e0;
  border-radius: 8px;
  background: #fdfdfd;
  transition: box-shadow 0.2s ease, transform 0.2s ease;
}

.category-item:hover {
  box-shadow: 0 4px 14px rgba(0, 0, 0, 0.08);
  transform: translateY(-2px);
}

.category-item--clickable {
  cursor: pointer;
}

.category-info {
  display: flex;
  align-items: flex-start;
  gap: 12px;
}

.category-color-dot {
  width: 16px;
  height: 16px;
  border-radius: 50%;
  border: 2px solid #fff;
  box-shadow: 0 0 4px rgba(0, 0, 0, 0.15);
  margin-top: 4px;
}

.category-text {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.category-name-line {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.category-name {
  font-weight: 600;
  color: #1f2933;
}

.category-stocks-preview {
  flex: 1 1 100%;
  font-size: 0.85em;
  color: #3f4a5a;
  margin-top: 4px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.category-count {
  font-size: 0.85em;
  color: #556987;
  background: #eef2ff;
  padding: 2px 8px;
  border-radius: 999px;
}

.category-description {
  font-size: 0.85em;
  color: #5f6c7b;
  max-width: 380px;
}

.category-summary {
  font-size: 0.9em;
  color: #546172;
}

.price-section {
  margin: 15px 0;
}

.ai-insight {
  display: flex;
  align-items: center;
  gap: 10px;
  margin: 10px 0 12px;
  padding: 10px 12px;
  background: #f7f8ff;
  border: 1px solid #e4e9ff;
  border-radius: 8px;
}

.rating-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 40px;
  padding: 4px 12px;
  border-radius: 999px;
  font-weight: 600;
  font-size: 0.85em;
  letter-spacing: 2px;
  text-transform: uppercase;
}

.rating-badge.excellence {
  color: #067647;
  background: #e0f7ec;
  border: 1px solid #6dd8ac;
}

.rating-badge.good {
  color: #2458b5;
  background: #e3ecff;
  border: 1px solid #a8c6ff;
}

.rating-badge.neutral {
  color: #6b7280;
  background: #f4f5f7;
  border: 1px solid #d4d7dd;
}

.rating-badge.risk {
  color: #b91c1c;
  background: #fde8e8;
  border: 1px solid #f8b4b4;
}

.action-chip {
  display: inline-flex;
  align-items: center;
  padding: 4px 12px;
  border-radius: 999px;
  font-size: 0.9em;
  font-weight: 500;
  color: #25304f;
  background: #ffffff;
  border: 1px solid #d9def3;
  box-shadow: 0 1px 3px rgba(37, 48, 79, 0.08);
}

.current-price {
  font-size: 2em;
  font-weight: bold;
  margin-bottom: 5px;
}

.price-info-row {
  display: flex;
  gap: 15px;
  margin-top: 10px;
  font-size: 0.9em;
}

.price-item {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.price-label {
  color: #666;
  font-size: 0.85em;
}

.price-value {
  font-weight: bold;
}

.price-up {
  color: #f44336;
}

.price-down {
  color: #4caf50;
}

.cost-info-section {
  margin-top: 15px;
  padding: 12px;
  background: #f9f9f9;
  border-radius: 6px;
  border: 1px solid #e0e0e0;
}

.cost-info-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 10px;
  font-weight: bold;
  font-size: 0.9em;
  color: #333;
}

.optional-fields-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  cursor: pointer;
  user-select: none;
  padding: 8px 12px;
  margin: -8px -12px;
  border-radius: 4px;
  transition: background-color 0.2s, color 0.2s;
  font-weight: 500;
  color: #666;
}

.optional-fields-header:hover {
  background-color: #f5f5f5;
  color: #1890ff;
}

.optional-fields-header .expand-icon {
  font-size: 0.9em;
  transition: transform 0.2s;
}

.optional-fields-content {
  margin-top: 12px;
  animation: slideDown 0.2s ease-out;
}

@keyframes slideDown {
  from {
    opacity: 0;
    max-height: 0;
    overflow: hidden;
  }
  to {
    opacity: 1;
    max-height: 500px;
  }
}

.cost-info-edit {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.cost-info {
  padding: 8px 12px;
  border-radius: 4px;
  font-size: 0.85em;
}

.cost-positive {
  background: #e8f5e9;
  color: #2e7d32;
}

.cost-negative {
  background: #ffebee;
  color: #c62828;
}

.cost-neutral {
  background: #f5f5f5;
  color: #666;
}

.suggested-price-section {
  margin-top: 15px;
  padding: 12px;
  background: #f9f9f9;
  border-radius: 6px;
  border: 1px solid #e0e0e0;
}

.suggested-price-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 10px;
  font-weight: bold;
  font-size: 0.9em;
  color: #333;
}

.btn-icon {
  background: none;
  border: none;
  cursor: pointer;
  font-size: 1.2em;
  color: #666;
  padding: 4px 8px;
  border-radius: 4px;
  transition: all 0.2s;
}

.btn-icon:hover {
  background: #e0e0e0;
  color: #333;
}

.suggested-price-edit {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.price-input-group {
  display: flex;
  align-items: center;
  gap: 8px;
}

.price-input-group label {
  min-width: 60px;
  font-size: 0.85em;
  color: #666;
}

.price-input {
  flex: 1;
  padding: 6px 10px;
  border: 1px solid #ddd;
  border-radius: 4px;
  font-size: 0.9em;
}

.price-input:focus {
  outline: none;
  border-color: #1890ff;
  box-shadow: 0 0 0 2px rgba(24, 144, 255, 0.2);
}

.suggested-price-display {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.suggested-price-item {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.9em;
  padding: 4px 0;
}

.suggested-price-item.buy-price .price-value {
  color: #4caf50;
  font-weight: bold;
}

.suggested-price-item.sell-price .price-value {
  color: #f44336;
  font-weight: bold;
}

.price-label {
  min-width: 50px;
  color: #666;
}

.price-value {
  flex: 1;
}

.alert-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 4px;
  margin-left: 8px;
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 0.75em;
  font-weight: 500;
}

.alert-icon {
  width: 16px;
  height: 16px;
  display: block;
  flex-shrink: 0;
}

.alert-text {
  white-space: nowrap;
  line-height: 1;
}

.alert-badge.alert-completed {
  color: #4caf50;
  background: rgba(76, 175, 80, 0.1);
  border: 1px solid rgba(76, 175, 80, 0.3);
}

.alert-badge.alert-completed .alert-icon {
  animation: starTwinkle 2s ease-in-out infinite;
}

.alert-badge.alert-triggered {
  color: #ff6b35;
  background: rgba(255, 107, 53, 0.1);
  border: 1px solid rgba(255, 107, 53, 0.3);
}

.alert-badge.alert-triggered .bell-icon {
  animation: bellRing 1s ease-in-out infinite;
  transform-origin: center top;
}

@keyframes starTwinkle {
  0%, 100% {
    opacity: 1;
    transform: scale(1);
    filter: brightness(1);
  }
  25% {
    opacity: 0.8;
    transform: scale(1.05);
    filter: brightness(1.2);
  }
  50% {
    opacity: 0.9;
    transform: scale(1.1);
    filter: brightness(1.3);
  }
  75% {
    opacity: 0.85;
    transform: scale(1.05);
    filter: brightness(1.2);
  }
}

@keyframes bellRing {
  0% {
    transform: rotate(0deg) scale(1);
    opacity: 1;
  }
  5%, 15% {
    transform: rotate(-12deg) scale(1.05);
  }
  10% {
    transform: rotate(12deg) scale(1.05);
  }
  20% {
    transform: rotate(-8deg) scale(1.02);
  }
  25% {
    transform: rotate(8deg) scale(1.02);
  }
  30%, 100% {
    transform: rotate(0deg) scale(1);
    opacity: 1;
  }
  50% {
    transform: rotate(0deg) scale(1.15);
    opacity: 0.95;
  }
}

.no-suggested-price {
  color: #999;
  font-size: 0.85em;
  font-style: italic;
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
  overflow: hidden;
  overscroll-behavior: contain;
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

.batch-modal {
  max-width: 760px;
  width: 92%;
}

.batch-form .form-group {
  margin-bottom: 16px;
}

.batch-form select,
.batch-form input[type="number"],
.batch-form textarea {
  width: 100%;
  padding: 10px;
  border: 1px solid #ddd;
  border-radius: 4px;
  font-size: 14px;
  box-sizing: border-box;
}

.batch-form textarea {
  min-height: 120px;
  resize: vertical;
  font-family: inherit;
}

.batch-form-row {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
}

.batch-form-row .form-group {
  flex: 1 1 200px;
  margin-bottom: 0;
}

.batch-form .checkbox {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 4px;
}

.error-text {
  color: #d32f2f;
  font-size: 0.9em;
  margin-bottom: 12px;
}

.batch-results {
  margin-top: 20px;
}

.batch-results table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9em;
  background: #fff;
  border: 1px solid #e0e0e0;
}

.batch-results th,
.batch-results td {
  padding: 8px 10px;
  border: 1px solid #e0e0e0;
  text-align: left;
}

.batch-results th {
  background: #f4f6ff;
  color: #324155;
  font-weight: 600;
}

.status-success {
  color: #2e7d32;
  font-weight: 600;
}

.status-failed {
  color: #c62828;
  font-weight: 600;
}

.status-neutral {
  color: #1f3c88;
  font-weight: 600;
}

.status-muted {
  color: #6b7280;
}

.batch-target-tip {
  margin-top: 12px;
  font-size: 0.9em;
  color: #5f6c7b;
}

.auto-trading-section {
  margin-top: 15px;
  padding: 12px;
  background: #f0f7ff;
  border-radius: 6px;
  border: 1px solid #b3d9ff;
}

.auto-trading-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 10px;
  font-weight: bold;
  font-size: 0.9em;
  color: #333;
}

.auto-trading-controls {
  display: flex;
  align-items: center;
  gap: 8px;
}

.interval-select {
  padding: 4px 8px;
  font-size: 0.85em;
  border: 1px solid #ddd;
  border-radius: 4px;
  background: white;
  cursor: pointer;
  transition: all 0.3s;
}

.interval-select:hover:not(:disabled) {
  border-color: #1890ff;
}

.interval-select:focus {
  outline: none;
  border-color: #1890ff;
  box-shadow: 0 0 0 2px rgba(24, 144, 255, 0.2);
}

.interval-select:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.toggle-switch {
  position: relative;
  display: inline-block;
  width: 44px;
  height: 24px;
}

.toggle-switch input {
  opacity: 0;
  width: 0;
  height: 0;
}

.toggle-slider {
  position: absolute;
  cursor: pointer;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: #ccc;
  transition: 0.3s;
  border-radius: 24px;
}

.toggle-slider:before {
  position: absolute;
  content: "";
  height: 18px;
  width: 18px;
  left: 3px;
  bottom: 3px;
  background-color: white;
  transition: 0.3s;
  border-radius: 50%;
}

.toggle-switch input:checked + .toggle-slider {
  background-color: #4caf50;
}

.toggle-switch input:checked + .toggle-slider:before {
  transform: translateX(20px);
}

.toggle-switch input:disabled + .toggle-slider {
  opacity: 0.6;
  cursor: not-allowed;
}

.trading-plan-display {
  margin-top: 10px;
  padding: 10px;
  background: white;
  border-radius: 4px;
  border: 1px solid #d0e7ff;
}

.trading-plan-item {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
  font-size: 0.9em;
}

.plan-label {
  min-width: 50px;
  color: #666;
  font-weight: 500;
}

.plan-value {
  font-weight: bold;
  font-size: 1.05em;
}

.plan-value.buy-price {
  color: #4caf50;
}

.plan-value.sell-price {
  color: #f44336;
}

.trading-plan-suggestion {
  margin-top: 8px;
  padding: 8px;
  background: #f9f9f9;
  border-radius: 4px;
  font-size: 0.85em;
  color: #555;
  line-height: 1.5;
}

.trading-plan-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 8px;
  padding-top: 8px;
  border-top: 1px solid #e0e0e0;
}

.plan-update-time {
  font-size: 0.75em;
  color: #999;
}

.btn-refresh-plan {
  background: none;
  border: none;
  cursor: pointer;
  font-size: 1.2em;
  padding: 4px 8px;
  border-radius: 4px;
  transition: all 0.2s;
}

.btn-refresh-plan:hover:not(:disabled) {
  background: #e0e0e0;
}

.btn-refresh-plan:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.trading-plan-loading {
  margin-top: 10px;
  padding: 10px;
  text-align: center;
  color: #666;
  font-size: 0.85em;
}

.operation-buttons-section {
  margin-top: 15px;
  padding: 12px;
  background: #f9f9f9;
  border-radius: 6px;
  border: 1px solid #e0e0e0;
}

.operation-buttons-header {
  font-weight: bold;
  font-size: 0.9em;
  color: #333;
  margin-bottom: 8px;
}

.operation-buttons {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.btn-operation {
  flex: 1;
  min-width: 100px;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  color: white;
  border: none;
  transition: all 0.3s;
}

.btn-operation:hover:not(:disabled) {
  background: linear-gradient(135deg, #764ba2 0%, #667eea 100%);
  transform: translateY(-2px);
  box-shadow: 0 4px 8px rgba(102, 126, 234, 0.3);
}

.btn-operation:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.operation-analysis-modal {
  max-width: 800px;
  width: 92%;
  max-height: 90vh;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  overscroll-behavior: contain;
}

.operation-analysis-content {
  max-height: 60vh;
  overflow-y: auto;
  overflow-x: hidden;
  padding: 10px 0;
  overscroll-behavior: contain;
  -webkit-overflow-scrolling: touch;
}

.cache-indicator {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  margin-bottom: 12px;
  background: #f0f7ff;
  border: 1px solid #b3d9ff;
  border-radius: 6px;
  font-size: 0.85em;
}

.cache-badge {
  color: #1890ff;
  font-weight: 600;
}

.cache-time {
  color: #666;
}

.operation-analysis-content::-webkit-scrollbar {
  width: 6px;
}

.operation-analysis-content::-webkit-scrollbar-track {
  background: #f1f1f1;
  border-radius: 3px;
}

.operation-analysis-content::-webkit-scrollbar-thumb {
  background: #888;
  border-radius: 3px;
}

.operation-analysis-content::-webkit-scrollbar-thumb:hover {
  background: #555;
}

.analysis-text {
  line-height: 1.8;
  color: #333;
  font-size: 0.95em;
}

.analysis-text p {
  margin: 10px 0;
}

.analysis-text h4 {
  margin: 15px 0 10px;
  color: #667eea;
  font-size: 1.1em;
  font-weight: 600;
}

.analysis-text ul {
  margin: 10px 0;
  padding-left: 20px;
}

.analysis-text li {
  margin: 5px 0;
  list-style-type: disc;
}

.analysis-text strong {
  color: #764ba2;
  font-weight: 600;
}

@media (max-width: 768px) {
  .content {
    padding: 15px;
  }
  
  .stock-grid {
    grid-template-columns: 1fr;
  }

  .operation-buttons {
    flex-direction: column;
  }

  .btn-operation {
    width: 100%;
  }
}
</style>


