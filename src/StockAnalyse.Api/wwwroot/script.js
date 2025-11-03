const API_BASE = window.location.origin;

// 全局变量
let currentNewsData = []; // 存储当前页面的新闻数据

// 自动刷新相关变量
let autoRefreshTimer = null;
let autoRefreshInterval = 3; // 默认3秒
let autoRefreshEnabled = true;

// AI分析设置相关变量
let selectedAIModelId = null;
let selectedAIPromptId = null;

// Tab切换
function switchTab(tabName, element) {
    document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
    document.querySelectorAll('.content').forEach(c => c.classList.remove('active'));
    
    // 如果没有传入element参数，尝试找到对应的tab元素
    let targetElement = element;
    if (!targetElement) {
        // 优先尝试通过onclick属性查找
        targetElement = document.querySelector(`[onclick*="switchTab('${tabName}')"]`);
        
        // 如果还是找不到，尝试通过标签顺序查找
        if (!targetElement) {
            const tabs = document.querySelectorAll('.tab');
            const tabMap = {
                'watchlist': 0,
                'screen': 1,
                'quant': 2,
                'news': 3,
                'ai': 4,
                'alert': 5,
                'settings': 6
            };
            const index = tabMap[tabName];
            if (index !== undefined && tabs[index]) {
                targetElement = tabs[index];
            }
        }
    }
    
    if (targetElement) {
        targetElement.classList.add('active');
    }
    
    const contentElement = document.getElementById(tabName);
    if (contentElement) {
        contentElement.classList.add('active');
    }
    
    // 根据Tab加载数据
    switch(tabName) {
        case 'watchlist':
            loadWatchlist();
            break;
        case 'quant':
            quantTrading.loadStrategies();
            // 设置默认日期
            const today = new Date();
            const oneMonthAgo = new Date(today.getTime() - 30 * 24 * 60 * 60 * 1000);
            document.getElementById('backtestStartDate').value = oneMonthAgo.toISOString().split('T')[0];
            document.getElementById('backtestEndDate').value = today.toISOString().split('T')[0];
            break;
        case 'news':
            loadNews();
            break;
        case 'alert':
            loadAlerts();
            break;
        case 'settings':
            loadNewsRefreshSettings();
            // 加载AI配置
            if (typeof aiConfigManager !== 'undefined') {
                aiConfigManager.loadConfigs();
            }
            break;
    }
}

// 添加自选股
async function addToWatchlist() {
    const stockCode = document.getElementById('stockCode').value.trim();
    const categoryId = document.getElementById('categorySelect').value;
    
    if (!stockCode || !categoryId) {
        alert('请填写股票代码和选择分类');
        return;
    }
    
    try {
        const response = await fetch(`${API_BASE}/api/watchlist/add`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ stockCode, categoryId: parseInt(categoryId) })
        });
        
        if (response.ok) {
            alert('添加成功！');
            document.getElementById('stockCode').value = '';
            loadWatchlist();
        } else {
            alert('添加失败：' + await response.text());
        }
    } catch (error) {
        alert('添加失败：' + error.message);
    }
}

// 加载自选股
async function loadWatchlist() {
    try {
        const response = await fetch(`${API_BASE}/api/watchlist/grouped`);
        
        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(`HTTP ${response.status}: ${errorText.substring(0, 100)}`);
        }
        
        const data = await response.json();
        console.log('加载的自选股数据：', data);
        
        let html = '';
        if (!data || Object.keys(data).length === 0) {
            html = '<p class="no-data">暂无自选股，请添加股票</p>';
        } else {
            for (const [category, stocks] of Object.entries(data)) {
                html += `<h4 class="category-title">${category}</h4>`;
                html += '<div class="stock-cards">';
                
                stocks.forEach(stock => {
                    const stockData = stock.stock || {};
                    const currentPrice = stockData.currentPrice || stockData?.currentPrice || 0;
                    const changePercent = stockData.changePercent || stockData?.changePercent || 0;
                    const isPositive = changePercent >= 0;
                    const categoryColor = stock.category?.color || '#667eea';
                    
                    // 验证价格数据是否有效
                    const isValidPrice = currentPrice && currentPrice > 0;
                    
                    const stockName = stockData?.name || stockData?.Name || 'N/A';
                    
                    html += `
                        <div class="stock-card" id="card-${stock.stockCode}">
                            <div class="category-label" style="background: ${categoryColor}20; color: ${categoryColor};">
                                ${category}
                            </div>
                            <div class="stock-header">
                                <div class="stock-name-section">
                                    <div class="stock-name">${stockData.name || 'N/A'}</div>
                                    <div class="stock-code">${stock.stockCode}</div>
                                </div>
                                <div class="stock-actions">
                                    <button class="btn btn-small" onclick="openAI('${stock.stockCode}')">AI分析</button>
                                    <button class="btn btn-danger btn-small" onclick="removeStock(${stock.id})">删除</button>
                                </div>
                            </div>
                            <div class="price-section">
                                <div class="current-price ${isValidPrice ? (isPositive ? 'price-up' : 'price-down') : ''}">
                                    ${isValidPrice ? currentPrice.toFixed(2) : '暂无数据'}
                                </div>
                                ${isValidPrice ? `
                                <div class="price-info-row">
                                    <div>
                                        <span class="${isPositive ? 'price-up' : 'price-down'}">
                                            ${changePercent >= 0 ? '+' : ''}${changePercent.toFixed(2)}%
                                        </span>
                                    </div>
                                    <div class="price-item">
                                        <span class="price-label">最高</span>
                                        <span class="price-value">${stockData.highPrice ? stockData.highPrice.toFixed(2) : 'N/A'}</span>
                                    </div>
                                    <div class="price-item">
                                        <span class="price-label">最低</span>
                                        <span class="price-value">${stockData.lowPrice ? stockData.lowPrice.toFixed(2) : 'N/A'}</span>
                                    </div>
                                </div>
                                <div class="price-info-row">
                                    <div class="price-item">
                                        <span class="price-label">今开</span>
                                        <span class="price-value">${stockData.openPrice ? stockData.openPrice.toFixed(2) : 'N/A'}</span>
                                    </div>
                                    <div class="price-item">
                                        <span class="price-label">昨收</span>
                                        <span class="price-value">${stockData.closePrice ? stockData.closePrice.toFixed(2) : (stockData.prevClose ? stockData.prevClose.toFixed(2) : 'N/A')}</span>
                                    </div>
                                </div>
                                ` : `
                                <div class="price-info-row">
                                    <div class="no-data">暂无有效价格数据</div>
                                </div>
                                `}
                            </div>
                            <div class="market-info">
                                <div style="color: #666;">
                                    成交量: ${stockData.volume ? (stockData.volume / 10000).toFixed(2) + '万' : 'N/A'}
                                </div>
                                <div class="cost-info ${stock.profitLoss >= 0 ? 'cost-positive' : 'cost-negative'}">
                                    成本: ${stock.costPrice ? stock.costPrice.toFixed(2) : '-'} × ${stock.quantity || '-'}<br>
                                    盈亏: <strong>${stock.profitLoss !== undefined ? stock.profitLoss.toFixed(2) : '-'}</strong>
                                    (${stock.profitLossPercent !== undefined ? stock.profitLossPercent.toFixed(2) : '-'}%)
                                </div>
                            </div>
                        </div>
                    `;
                });
                html += '</div>';
            }
        }
        
        document.getElementById('watchlistList').innerHTML = html;
        
        // 填充回测下拉框
        try {
            const codes = Object.values(data || {})
                .flatMap(arr => (Array.isArray(arr) ? arr : []))
                .map(s => s.stockCode)
                .filter(Boolean);

            const uniqueCodes = Array.from(new Set(codes));
            const sel = document.getElementById('backtestStockSelect');
            if (sel) {
                // 保留当前选中的值
                const currentSelected = Array.from(sel.selectedOptions || [])
                    .map(opt => opt.value)
                    .filter(Boolean);
                    
                sel.innerHTML = uniqueCodes.map(c => 
                    `<option value="${c}" ${currentSelected.includes(c) ? 'selected' : ''}>${c}</option>`
                ).join('');
            }
        } catch (e) {
            console.warn('填充回测股票下拉失败:', e);
        }
    } catch (error) {
        document.getElementById('watchlistList').innerHTML = '<p class="error">加载失败：' + error.message + '</p>';
    }
}

// 条件选股 - 分页状态
let currentScreenPage = 1;
let currentScreenPageSize = 20;
let currentScreenCriteria = null;

// 条件选股
async function screenStocks(pageIndex = 1) {
    // 保存当前条件（如果正在翻页，使用保存的条件）
    if (pageIndex === 1) {
        currentScreenCriteria = {
            market: document.getElementById('market').value || null,
            minPrice: parseFloat(document.getElementById('minPrice').value) || null,
            maxPrice: parseFloat(document.getElementById('maxPrice').value) || null,
            minChangePercent: parseFloat(document.getElementById('minChange').value) || null,
            maxChangePercent: parseFloat(document.getElementById('maxChange').value) || null,
            minTurnoverRate: parseFloat(document.getElementById('minTurnover').value) || null,
            maxTurnoverRate: parseFloat(document.getElementById('maxTurnover').value) || null,
            minVolume: parseFloat(document.getElementById('minVolume').value) || null,
            maxVolume: parseFloat(document.getElementById('maxVolume').value) || null,
            minMarketValue: parseFloat(document.getElementById('minMarketValue').value) || null,
            maxMarketValue: parseFloat(document.getElementById('maxMarketValue').value) || null,
            minDividendYield: parseFloat(document.getElementById('minDividendYield').value) || null,
            maxDividendYield: parseFloat(document.getElementById('maxDividendYield').value) || null,
            pageIndex: pageIndex,
            pageSize: currentScreenPageSize
        };
    } else {
        // 使用保存的条件，只更新页码
        currentScreenCriteria.pageIndex = pageIndex;
    }
    
    currentScreenPage = pageIndex;
    
    try {
        document.getElementById('screenResults').innerHTML = 
            '<div class="loading">⏳ 正在从东方财富获取实时股票数据并筛选...<br><small>这可能需要几秒钟时间</small></div>';
        
        const response = await fetch(`${API_BASE}/api/screen/search`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(currentScreenCriteria)
        });
        
        if (!response.ok) {
            const errorData = await response.json().catch(() => ({ error: '未知错误', message: response.statusText }));
            throw new Error(errorData.message || errorData.error || '查询失败');
        }
        
        const result = await response.json();
        
        if (result.items && result.items.length === 0 && result.totalCount === 0) {
            let tip = '<p class="warning">⚠️ 未找到符合条件的股票</p>';
            tip += '<p style="font-size: 0.9em; color: #666; margin-top: 10px;">可能的原因：</p>';
            tip += '<ul style="font-size: 0.9em; color: #666; margin-left: 20px;">';
            tip += '<li>可能从东方财富获取的股票数据中没有符合条件的股票</li>';
            if (currentScreenCriteria.market) {
                tip += `<li>市场筛选为：${currentScreenCriteria.market}，可能该市场暂无股票数据</li>`;
            }
            tip += '<li>其他筛选条件过于严格，导致没有股票满足所有条件</li>';
            tip += '<li>提示：可以查看服务器日志获取更详细的筛选信息</li>';
            tip += '</ul>';
            document.getElementById('screenResults').innerHTML = tip;
            return;
        }
        
        const stocks = result.items || [];
        const totalCount = result.totalCount || 0;
        const currentPage = result.pageIndex || 1;
        const pageSize = result.pageSize || 20;
        const totalPages = result.totalPages || 1;
        
        let html = `<p style="margin-bottom: 15px;">
            <strong>找到 ${totalCount} 只股票</strong>
            <span style="margin-left: 15px; color: #666; font-size: 0.9em;">
                第 ${currentPage} / ${totalPages} 页，每页 ${pageSize} 条
            </span>
        </p>`;
        
        html += '<div style="overflow-x: auto;"><table><thead><tr><th>代码</th><th>名称</th><th>当前价</th><th>涨跌幅</th><th>换手率</th><th>PE</th><th>PB</th><th>成交量</th></tr></thead><tbody>';
        
        stocks.forEach(stock => {
            html += `
                <tr>
                    <td>${stock.code}</td>
                    <td>${stock.name}</td>
                    <td>${stock.currentPrice.toFixed(2)}</td>
                    <td class="${stock.changePercent >= 0 ? 'price-up' : 'price-down'}">
                        ${stock.changePercent.toFixed(2)}%
                    </td>
                    <td>${stock.turnoverRate.toFixed(2)}%</td>
                    <td>${stock.pe?.toFixed(2) || '-'}</td>
                    <td>${stock.pb?.toFixed(2) || '-'}</td>
                    <td>${(stock.volume / 10000).toFixed(2)}万手</td>
                </tr>
            `;
        });
        
        html += '</tbody></table></div>';
        
        // 添加分页控件
        html += '<div style="margin-top: 20px; text-align: center;">';
        html += renderPagination(currentPage, totalPages, totalCount);
        html += '</div>';
        
        document.getElementById('screenResults').innerHTML = html;
    } catch (error) {
        console.error('选股查询错误:', error);
        document.getElementById('screenResults').innerHTML = 
            '<p class="error">查询失败：' + error.message + '</p>' +
            '<p style="font-size: 0.9em; color: #666; margin-top: 10px;">请检查网络连接或查看服务器日志</p>';
    }
}

// 渲染分页控件
function renderPagination(currentPage, totalPages, totalCount) {
    if (totalPages <= 1) {
        return '';
    }
    
    let html = '<div style="display: inline-flex; align-items: center; gap: 5px;">';
    
    // 首页和上一页
    if (currentPage > 1) {
        html += `<button onclick="screenStocks(1)" style="padding: 5px 10px; cursor: pointer;">首页</button>`;
        html += `<button onclick="screenStocks(${currentPage - 1})" style="padding: 5px 10px; cursor: pointer;">上一页</button>`;
    } else {
        html += `<button disabled style="padding: 5px 10px; opacity: 0.5;">首页</button>`;
        html += `<button disabled style="padding: 5px 10px; opacity: 0.5;">上一页</button>`;
    }
    
    // 页码按钮（显示当前页前后各2页）
    let startPage = Math.max(1, currentPage - 2);
    let endPage = Math.min(totalPages, currentPage + 2);
    
    if (startPage > 1) {
        html += `<button onclick="screenStocks(1)" style="padding: 5px 10px; cursor: pointer;">1</button>`;
        if (startPage > 2) {
            html += `<span style="padding: 5px;">...</span>`;
        }
    }
    
    for (let i = startPage; i <= endPage; i++) {
        if (i === currentPage) {
            html += `<button style="padding: 5px 10px; background: #007bff; color: white; border: none; cursor: pointer; font-weight: bold;">${i}</button>`;
        } else {
            html += `<button onclick="screenStocks(${i})" style="padding: 5px 10px; cursor: pointer;">${i}</button>`;
        }
    }
    
    if (endPage < totalPages) {
        if (endPage < totalPages - 1) {
            html += `<span style="padding: 5px;">...</span>`;
        }
        html += `<button onclick="screenStocks(${totalPages})" style="padding: 5px 10px; cursor: pointer;">${totalPages}</button>`;
    }
    
    // 下一页和末页
    if (currentPage < totalPages) {
        html += `<button onclick="screenStocks(${currentPage + 1})" style="padding: 5px 10px; cursor: pointer;">下一页</button>`;
        html += `<button onclick="screenStocks(${totalPages})" style="padding: 5px 10px; cursor: pointer;">末页</button>`;
    } else {
        html += `<button disabled style="padding: 5px 10px; opacity: 0.5;">下一页</button>`;
        html += `<button disabled style="padding: 5px 10px; opacity: 0.5;">末页</button>`;
    }
    
    // 每页数量选择
    html += `<span style="margin-left: 15px;">每页：</span>`;
    html += `<select id="screenPageSize" onchange="changeScreenPageSize()" style="padding: 3px;">`;
    html += `<option value="10" ${currentScreenPageSize === 10 ? 'selected' : ''}>10</option>`;
    html += `<option value="20" ${currentScreenPageSize === 20 ? 'selected' : ''}>20</option>`;
    html += `<option value="50" ${currentScreenPageSize === 50 ? 'selected' : ''}>50</option>`;
    html += `<option value="100" ${currentScreenPageSize === 100 ? 'selected' : ''}>100</option>`;
    html += `</select>`;
    
    html += '</div>';
    return html;
}

// 改变每页数量
function changeScreenPageSize() {
    const newSize = parseInt(document.getElementById('screenPageSize').value);
    currentScreenPageSize = newSize;
    if (currentScreenCriteria) {
        currentScreenCriteria.pageSize = newSize;
        screenStocks(1); // 重新从第一页开始
    }
}

// 加载新闻
async function loadNews() {
    try {
        document.getElementById('newsList').innerHTML = '<div class="loading">加载中...</div>';
        const response = await fetch(`${API_BASE}/api/news/latest?count=50`);
        const news = await response.json();
        
        if (news && news.length > 0) {
            currentNewsData = news; // 存储新闻数据
            displayNews(news);
        } else {
            currentNewsData = []; // 清空数据
            // 如果没有新闻数据，尝试自动抓取
            await fetchLatestNews();
        }
    } catch (error) {
        currentNewsData = []; // 清空数据
        document.getElementById('newsList').innerHTML = '<p class="error">加载失败：' + error.message + '</p>';
    }
}

// 显示新闻列表
function displayNews(news) {
    let html = '';
    if (news && news.length > 0) {
        news.forEach(item => {
            const stockCodes = item.stockCodes ? item.stockCodes.join(', ') : '';
            const content = item.content || item.title;
            html += `
                <div class="news-item" onclick="openNewsDetail('${item.id}')">
                    <strong class="news-title">${item.title}</strong>
                    <p class="news-content">${content.substring(0, 120)}${content.length > 120 ? '...' : ''}</p>
                    <div class="news-meta">
                        <span class="news-source">
                            📰 ${item.source} · ${new Date(item.publishTime).toLocaleString()}
                            ${stockCodes ? ' · 📈 ' + stockCodes : ''}
                        </span>
                        <div>
                            <span class="news-views">👁️ ${item.viewCount || 0}</span>
                        </div>
                    </div>
                </div>
            `;
        });
    } else {
        html = '<p class="no-data">暂无新闻数据，请点击"抓取最新新闻"按钮获取最新金融消息</p>';
    }
    
    document.getElementById('newsList').innerHTML = html;
}

// 搜索新闻
async function searchNews() {
    const keyword = document.getElementById('newsSearch').value.trim();
    if (!keyword) {
        alert('请输入搜索关键词');
        return;
    }
    
    try {
        document.getElementById('newsList').innerHTML = '<div class="loading">搜索中...</div>';
        const response = await fetch(`${API_BASE}/api/news/search?keyword=${encodeURIComponent(keyword)}`);
        const news = await response.json();
        
        currentNewsData = news || []; // 更新当前新闻数据
        displayNews(news);
    } catch (error) {
        currentNewsData = []; // 清空数据
        document.getElementById('newsList').innerHTML = '<p class="error">搜索失败：' + error.message + '</p>';
    }
}

// 抓取最新新闻
async function fetchLatestNews() {
    try {
        document.getElementById('newsList').innerHTML = '<div class="loading">正在从天行数据和新浪财经抓取最新新闻...</div>';
        
        const response = await fetch(`${API_BASE}/api/news/fetch`, {
            method: 'POST'
        });
        
        if (response.ok) {
            const result = await response.json();
            alert(result.message || '新闻抓取任务已启动，请稍后刷新查看');
            
            // 等待2秒后刷新新闻列表
            setTimeout(() => {
                loadNews();
            }, 2000);
        } else {
            throw new Error(await response.text());
        }
    } catch (error) {
        document.getElementById('newsList').innerHTML = '<p class="error">抓取失败：' + error.message + '</p>';
    }
}

// 批量AI分析新闻 - 显示提示词选择对话框
async function analyzeBatchNews() {
    try {
        // 首先加载提示词列表
        await loadPrompts();
        
        // 显示提示词选择对话框
        document.getElementById('promptSelectionModal').style.display = 'block';
        
    } catch (error) {
        alert('加载提示词失败：' + error.message);
    }
}

// 加载提示词列表
async function loadPrompts() {
    try {
        const response = await fetch(`${API_BASE}/api/news/prompts`);
        if (!response.ok) {
            throw new Error('获取提示词列表失败');
        }
        
        const prompts = await response.json();
        const promptList = document.getElementById('promptList');
        
        let html = '';
        
        // 添加默认选项
        html += `
            <div class="prompt-option" onclick="selectPrompt(null, '默认分析', '使用系统默认的金融新闻分析提示词')">
                <input type="radio" name="promptSelection" value="" style="margin-right: 8px;">
                <strong>默认分析</strong>
                <p>使用系统默认的金融新闻分析提示词</p>
            </div>
        `;
        
        // 添加自定义提示词
        prompts.forEach(prompt => {
            html += `
                <div class="prompt-option" onclick="selectPrompt(${prompt.id}, '${prompt.name}', '${prompt.description}')">
                    <input type="radio" name="promptSelection" value="${prompt.id}" style="margin-right: 8px;">
                    <strong>${prompt.name}</strong>
                    <p>${prompt.description}</p>
                </div>
            `;
        });
        
        promptList.innerHTML = html;
        
    } catch (error) {
        throw error;
    }
}

// 选择提示词
let selectedPromptId = null;
function selectPrompt(promptId, promptName, promptDescription) {
    selectedPromptId = promptId;
    
    // 更新单选按钮状态
    document.querySelectorAll('input[name="promptSelection"]').forEach(radio => {
        radio.checked = (radio.value == (promptId || ''));
    });
    
    // 启用确认按钮
    document.getElementById('confirmPromptBtn').disabled = false;
}

// 关闭提示词选择对话框
function closePromptModal() {
    document.getElementById('promptSelectionModal').style.display = 'none';
    selectedPromptId = null;
    document.getElementById('confirmPromptBtn').disabled = true;
}

// 确认提示词选择并开始分析
async function confirmPromptSelection() {
    try {
        // 关闭对话框
        closePromptModal();
        
        // 获取当前页面的所有新闻ID
        const newsIds = currentNewsData.map(news => news.id);
        
        if (newsIds.length === 0) {
            alert('当前页面没有新闻数据，请先加载新闻');
            return;
        }

        // 显示加载状态
        const resultDiv = document.getElementById('batchAnalysisResult');
        const contentDiv = document.getElementById('batchAnalysisContent');
        const statsDiv = document.getElementById('batchAnalysisStats');
        
        resultDiv.classList.remove('hidden');
        contentDiv.innerHTML = '🤖 正在进行市场综合分析，请稍候...';
        statsDiv.innerHTML = '';

        // 调用批量分析API
        const requestBody = {
            newsIds: newsIds
        };
        
        if (selectedPromptId) {
            requestBody.promptId = selectedPromptId;
        }

        const response = await fetch(`${API_BASE}/api/news/analyze-batch`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(requestBody)
        });

        if (!response.ok) {
            throw new Error('批量分析请求失败');
        }

        const result = await response.json();
        
        // 显示分析结果
        contentDiv.innerHTML = result.analysis || result;
        
        // 显示统计信息
        if (result.newsCount !== undefined) {
            const timeRange = result.timeRange;
            const hotStocks = result.hotStocks || [];
            
            let statsHtml = `📊 分析了 ${result.newsCount} 条新闻`;
            if (timeRange) {
                const fromTime = new Date(timeRange.from).toLocaleString();
                const toTime = new Date(timeRange.to).toLocaleString();
                statsHtml += ` | ⏰ 时间范围: ${fromTime} ~ ${toTime}`;
            }
            if (hotStocks.length > 0) {
                statsHtml += ` | 🔥 热门股票: ${hotStocks.slice(0, 5).join(', ')}`;
            }
            
            statsDiv.innerHTML = statsHtml;
        }

    } catch (error) {
        const contentDiv = document.getElementById('batchAnalysisContent');
        contentDiv.innerHTML = `❌ 批量分析失败：${error.message}`;
    }
}

// 隐藏批量分析结果
function hideBatchAnalysis() {
    document.getElementById('batchAnalysisResult').classList.add('hidden');
}

// 打开新闻详情
function openNewsDetail(newsId) {
    alert('新闻详情功能待开发，新闻ID: ' + newsId);
}

// 更新新闻刷新设置
async function updateNewsRefreshSettings() {
    const intervalMinutes = document.getElementById('newsRefreshInterval').value;
    const enabled = document.getElementById('enableNewsAutoRefresh').checked;
    
    try {
        const response = await fetch(`${API_BASE}/api/news/refresh-settings`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                intervalMinutes: parseInt(intervalMinutes),
                enabled: enabled
            })
        });
        
        if (response.ok) {
            alert('新闻刷新设置已更新！');
        } else {
            throw new Error(await response.text());
        }
    } catch (error) {
        alert('更新失败：' + error.message);
    }
}

// 强制立即刷新新闻
async function forceRefreshNews() {
    try {
        const response = await fetch(`${API_BASE}/api/news/fetch`, {
            method: 'POST'
        });
        
        if (response.ok) {
            alert('新闻刷新任务已启动，请稍后查看新闻页面');
            
            // 等待3秒后刷新新闻列表
            setTimeout(() => {
                if (document.getElementById('news').classList.contains('active')) {
                    loadNews();
                }
            }, 3000);
        } else {
            throw new Error(await response.text());
        }
    } catch (error) {
        alert('刷新失败：' + error.message);
    }
}

// 加载新闻刷新设置
async function loadNewsRefreshSettings() {
    try {
        const response = await fetch(`${API_BASE}/api/news/refresh-settings`);
        if (response.ok) {
            const settings = await response.json();
            document.getElementById('newsRefreshInterval').value = settings.intervalMinutes || 30;
            document.getElementById('enableNewsAutoRefresh').checked = settings.enabled !== false;
        }
    } catch (error) {
        console.error('加载新闻刷新设置失败：', error);
    }
}

// AI分析
async function analyzeStock() {
    const stockCode = document.getElementById('aiStockCode').value.trim();
    const promptIdVal = document.getElementById('aiPromptSelect').value;
    const promptId = promptIdVal ? parseInt(promptIdVal) : null;
    const modelId = selectedAIModelId || null;

    if (!stockCode) {
        alert('请输入股票代码');
        return;
    }

    try {
        document.getElementById('aiResult').textContent = '分析中...';
        const response = await fetch(`${API_BASE}/api/ai/analyze/${stockCode}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ promptId, context: "", modelId })
        });
        const result = await response.text();
        document.getElementById('aiResult').textContent = result;
    } catch (error) {
        document.getElementById('aiResult').textContent = '分析失败：' + error.message;
    }
}

// 创建提醒
async function createAlert() {
    const stockCode = document.getElementById('alertStockCode').value.trim();
    const targetPrice = parseFloat(document.getElementById('alertPrice').value);
    const type = document.getElementById('alertType').value;
    
    if (!stockCode || !targetPrice) {
        alert('请填写完整信息');
        return;
    }
    
    try {
        const response = await fetch(`${API_BASE}/api/alert/create`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ stockCode, targetPrice, type })
        });
        
        if (response.ok) {
            alert('创建成功！');
            document.getElementById('alertStockCode').value = '';
            document.getElementById('alertPrice').value = '';
            loadAlerts();
        } else {
            alert('创建失败：' + await response.text());
        }
    } catch (error) {
        alert('创建失败：' + error.message);
    }
}

// 加载提醒
async function loadAlerts() {
    try {
        const response = await fetch(`${API_BASE}/api/alert/active`);
        const alerts = await response.json();
        
        let html = '<table><thead><tr><th>股票代码</th><th>目标价</th><th>类型</th><th>状态</th><th>创建时间</th><th>操作</th></tr></thead><tbody>';
        
        alerts.forEach(alert => {
            html += `
                <tr>
                    <td>${alert.stockCode}</td>
                    <td>${alert.targetPrice.toFixed(2)}</td>
                    <td>${getAlertTypeName(alert.type)}</td>
                    <td>${alert.isTriggered ? '已触发' : '待触发'}</td>
                    <td>${new Date(alert.createTime).toLocaleString()}</td>
                    <td><button class="btn btn-danger" onclick="deleteAlert(${alert.id})">删除</button></td>
                </tr>
            `;
        });
        
        html += '</tbody></table>';
        document.getElementById('alertsList').innerHTML = html || '<p class="no-data">暂无提醒</p>';
    } catch (error) {
        document.getElementById('alertsList').innerHTML = '<p class="error">加载失败：' + error.message + '</p>';
    }
}

function getAlertTypeName(type) {
    const types = {
        'PriceUp': '价格上涨',
        'PriceDown': '价格下跌',
        'PriceReach': '到达价格'
    };
    return types[type] || type;
}

function removeStock(id) {
    if (confirm('确定要删除吗？')) {
        fetch(`${API_BASE}/api/watchlist/${id}`, { method: 'DELETE' })
            .then(() => loadWatchlist());
    }
}

function deleteAlert(id) {
    if (confirm('确定要删除吗？')) {
        fetch(`${API_BASE}/api/alert/${id}`, { method: 'DELETE' })
            .then(() => loadAlerts());
    }
}

// 刷新自选股
async function refreshWatchlist() {
    const watchlistList = document.getElementById('watchlistList');
    watchlistList.innerHTML = '<div class="loading">刷新中...</div>';
    
    try {
        const response = await fetch(`${API_BASE}/api/watchlist/grouped`);
        
        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(`HTTP ${response.status}: ${errorText.substring(0, 100)}`);
        }
        
        const data = await response.json();
        console.log('刷新后的自选股数据：', data);
        
        let html = '';
        if (!data || Object.keys(data).length === 0) {
            html = '<p class="no-data">暂无自选股，请添加股票</p>';
        } else {
            for (const [category, stocks] of Object.entries(data)) {
                html += `<h4 class="category-title">${category}</h4>`;
                html += '<div class="stock-cards">';
                
                // 批量刷新股票行情
                for (const stock of stocks) {
                    try {
                        await fetch(`${API_BASE}/api/stock/${stock.stockCode}`);
                    } catch (e) {
                        console.error('刷新股票行情失败：', stock.stockCode, e);
                    }
                }
                
                // 重新获取更新后的数据
                const updatedResponse = await fetch(`${API_BASE}/api/watchlist/grouped`);
                const updatedData = await updatedResponse.json();
                const categoryStocks = updatedData[category] || stocks;
                
                categoryStocks.forEach(stock => {
                    const profitClass = (stock.profitLoss || 0) >= 0 ? 'price-up' : 'price-down';
                    const stockData = stock.stock || {};
                    const currentPrice = stockData.currentPrice || 0;
                    const changePercent = stockData.changePercent || 0;
                    const isPositive = changePercent >= 0;
                    const categoryColor = stock.category?.color || '#667eea';
                    
                    html += `
                        <div class="stock-card">
                            <div class="category-label" style="background: ${categoryColor}20; color: ${categoryColor};">
                                ${category}
                            </div>
                            <div class="stock-header">
                                <div class="stock-name-section">
                                    <div class="stock-name">${stockData.name || 'N/A'}</div>
                                    <div class="stock-code">${stock.stockCode}</div>
                                </div>
                                <div class="stock-actions">
                                    <button class="btn btn-small" onclick="openAI('${stock.stockCode}')">AI分析</button>
                                    <button class="btn btn-danger btn-small" onclick="removeStock(${stock.id})">删除</button>
                                </div>
                            </div>
                            <div class="price-section">
                                <div class="current-price ${isPositive ? 'price-up' : 'price-down'}">
                                    ${currentPrice.toFixed(2)}
                                </div>
                                <div class="price-info-row">
                                    <div>
                                        <span class="${isPositive ? 'price-up' : 'price-down'}">
                                            ${changePercent >= 0 ? '+' : ''}${changePercent.toFixed(2)}%
                                        </span>
                                    </div>
                                    <div class="price-item">
                                        <span class="price-label">最高</span>
                                        <span class="price-value">${stockData.highPrice ? stockData.highPrice.toFixed(2) : 'N/A'}</span>
                                    </div>
                                    <div class="price-item">
                                        <span class="price-label">最低</span>
                                        <span class="price-value">${stockData.lowPrice ? stockData.lowPrice.toFixed(2) : 'N/A'}</span>
                                    </div>
                                </div>
                                <div class="price-info-row">
                                    <div class="price-item">
                                        <span class="price-label">昨收</span>
                                        <span class="price-value">${stockData.closePrice ? stockData.closePrice.toFixed(2) : (stockData.prevClose ? stockData.prevClose.toFixed(2) : 'N/A')}</span>
                                    </div>
                                    <div class="price-item">
                                        <span class="price-label">今开</span>
                                        <span class="price-value">${stockData.openPrice ? stockData.openPrice.toFixed(2) : 'N/A'}</span>
                                    </div>
                                </div>
                            </div>
                            <div class="market-info">
                                <div style="color: #666;">
                                    成交量: ${stockData.volume ? (stockData.volume / 10000).toFixed(2) + '万' : 'N/A'}
                                </div>
                                <div class="cost-info ${stock.profitLoss >= 0 ? 'cost-positive' : 'cost-negative'}">
                                    成本: ${stock.costPrice ? stock.costPrice.toFixed(2) : '-'} × ${stock.quantity || '-'}<br>
                                    盈亏: <strong>${stock.profitLoss !== undefined ? stock.profitLoss.toFixed(2) : '-'}</strong>
                                    (${stock.profitLossPercent !== undefined ? stock.profitLossPercent.toFixed(2) : '-'}%)
                                </div>
                            </div>
                        </div>
                    `;
                });
                html += '</div>';
            }
        }
        
        watchlistList.innerHTML = html;
        updateLastRefreshTime(); // 更新刷新时间
    } catch (error) {
        watchlistList.innerHTML = '<p class="error">刷新失败：' + error.message + '</p>';
    }
}

// 打开AI分析
function openAI(stockCode) {
    // 切换到AI分析标签
    document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
    document.querySelectorAll('.content').forEach(c => c.classList.remove('active'));
    
    // 找到AI分析标签页（第5个，索引为4）
    const tabs = document.querySelectorAll('.tab');
    if (tabs.length > 4) {
        tabs[4].classList.add('active');
    }
    
    // 显示AI分析内容
    const aiContent = document.getElementById('ai');
    if (aiContent) {
        aiContent.classList.add('active');
    }
    
    // 设置股票代码
    document.getElementById('aiStockCode').value = stockCode;
    
    // 刷新提示词列表（防止未初始化）
    if (typeof aiPromptManager !== 'undefined') {
        aiPromptManager.fillPromptSelect();
    }
    
    // 自动分析
    analyzeStock();
}

// 显示创建分类对话框
function showCreateCategory() {
    document.getElementById('createCategoryModal').style.display = 'flex';
}

// 隐藏创建分类对话框
function hideCreateCategory() {
    document.getElementById('createCategoryModal').style.display = 'none';
    document.getElementById('categoryName').value = '';
    document.getElementById('categoryDesc').value = '';
    document.getElementById('categoryColor').value = '#1890ff';
}

// 创建分类
async function createCategory() {
    const name = document.getElementById('categoryName').value.trim();
    const desc = document.getElementById('categoryDesc').value.trim();
    const color = document.getElementById('categoryColor').value;
    
    if (!name) {
        alert('请输入分类名称');
        return;
    }
    
    try {
        const response = await fetch(`${API_BASE}/api/watchlist/categories`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name, description: desc, color })
        });
        
        if (response.ok) {
            alert('分类创建成功！');
            hideCreateCategory();
            loadCategories(); // 重新加载分类列表
        } else {
            alert('创建失败：' + await response.text());
        }
    } catch (error) {
        alert('创建失败：' + error.message);
    }
}

// 加载分类
async function loadCategories() {
    try {
        const response = await fetch(`${API_BASE}/api/watchlist/categories`);
        const categories = await response.json();
        
        let html = '<option value="">请选择分类</option>';
        if (categories.length === 0) {
            html += '<option value="" disabled>暂无分类，请先创建分类</option>';
        } else {
            categories.forEach(cat => {
                html += `<option value="${cat.id}" style="background: ${cat.color}20;">${cat.name}</option>`;
            });
        }
        
        document.getElementById('categorySelect').innerHTML = html;
    } catch (error) {
        console.error('加载分类失败：', error);
        document.getElementById('categorySelect').innerHTML = '<option value="">加载失败，请刷新重试</option>';
    }
}

// 自动刷新相关函数
function startAutoRefresh() {
    if (autoRefreshTimer) {
        clearInterval(autoRefreshTimer);
    }
    
    if (autoRefreshEnabled) {
        autoRefreshTimer = setInterval(() => {
            if (document.getElementById('watchlist').classList.contains('active')) {
                refreshStockCards(); // 只刷新卡片数据，不重新加载整个列表
            }
        }, autoRefreshInterval * 1000);
    }
}

// 只刷新股票卡片数据，不重新加载整个页面
async function refreshStockCards() {
    try {
        const response = await fetch(`${API_BASE}/api/watchlist/grouped`);
        if (!response.ok) return;
        
        const data = await response.json();
        
        for (const [category, stocks] of Object.entries(data)) {
            for (const stock of stocks) {
                const stockData = stock.stock || {};
                const cardId = `card-${stock.stockCode}`;
                const card = document.getElementById(cardId);
                
                if (card) {
                    // 更新价格
                    const currentPriceEl = card.querySelector('.current-price');
                    if (currentPriceEl) {
                        const price = stockData.currentPrice || 0;
                        const changePercent = stockData.changePercent || 0;
                        const isPositive = changePercent >= 0;
                        currentPriceEl.textContent = price.toFixed(2);
                        currentPriceEl.className = `current-price ${isPositive ? 'price-up' : 'price-down'}`;
                    }
                    
                    // 更新涨跌幅
                    const changePercentEl = card.querySelector('.price-info-row > div > span');
                    if (changePercentEl && stockData.changePercent !== undefined) {
                        const changePercent = stockData.changePercent || 0;
                        changePercentEl.textContent = `${changePercent >= 0 ? '+' : ''}${changePercent.toFixed(2)}%`;
                        changePercentEl.className = changePercent >= 0 ? 'price-up' : 'price-down';
                    }
                    
                    // 更新其他价格信息
                    const priceItems = card.querySelectorAll('.price-item .price-value');
                    if (priceItems.length >= 4) {
                        // 更新最高价
                        if (stockData.highPrice !== undefined) {
                            priceItems[0].textContent = stockData.highPrice.toFixed(2);
                        }
                        // 更新最低价
                        if (stockData.lowPrice !== undefined) {
                            priceItems[1].textContent = stockData.lowPrice.toFixed(2);
                        }
                        // 更新今开价
                        if (stockData.openPrice !== undefined) {
                            priceItems[2].textContent = stockData.openPrice.toFixed(2);
                        }
                        // 更新昨收价
                        const closePrice = stockData.closePrice !== undefined ? stockData.closePrice : stockData.prevClose;
                        if (closePrice !== undefined) {
                            priceItems[3].textContent = closePrice.toFixed(2);
                        }
                    }
                    
                    // 更新盈亏
                    if (stock.profitLoss !== undefined) {
                        const costInfoEl = card.querySelector('.cost-info');
                        if (costInfoEl) {
                            const profitClass = stock.profitLoss >= 0 ? 'cost-positive' : 'cost-negative';
                            costInfoEl.className = `cost-info ${profitClass}`;
                            
                            const profitStrong = costInfoEl.querySelector('strong');
                            if (profitStrong) {
                                profitStrong.textContent = stock.profitLoss.toFixed(2);
                            }
                            
                            const profitPercent = costInfoEl.textContent.match(/\(([^)]+)\)$/);
                            if (stock.profitLossPercent !== undefined && profitStrong) {
                                const parent = profitStrong.parentElement;
                                if (parent) {
                                    parent.textContent = parent.textContent.replace(/\([^)]+\)/, `(${stock.profitLossPercent.toFixed(2)}%)`);
                                }
                            }
                        }
                    }
                }
            }
        }
        
        updateLastRefreshTime();
    } catch (error) {
        console.error('刷新股票卡片失败：', error);
    }
}

function toggleAutoRefresh() {
    autoRefreshEnabled = !autoRefreshEnabled;
    const btn = document.getElementById('toggleRefreshBtn');
    const status = document.getElementById('refreshStatus');
    
    if (autoRefreshEnabled) {
        btn.innerHTML = '⏸️ 暂停';
        status.textContent = '已启用';
        status.style.color = '#4caf50';
        startAutoRefresh();
    } else {
        btn.innerHTML = '▶️ 继续';
        status.textContent = '已暂停';
        status.style.color = '#f44336';
        if (autoRefreshTimer) {
            clearInterval(autoRefreshTimer);
        }
    }
}

function saveSettings() {
    autoRefreshInterval = parseFloat(document.getElementById('autoRefreshInterval').value);
    autoRefreshEnabled = document.getElementById('enableAutoRefresh').checked;
    
    // 更新显示
    document.getElementById('refreshInterval').textContent = autoRefreshInterval;
    document.getElementById('currentInterval').textContent = autoRefreshInterval + '秒';
    document.getElementById('currentStatus').textContent = autoRefreshEnabled ? '已启用' : '已禁用';
    document.getElementById('currentStatus').style.color = autoRefreshEnabled ? '#4caf50' : '#f44336';
    
    // 重启自动刷新
    startAutoRefresh();
    
    // 保存到本地存储
    localStorage.setItem('autoRefreshInterval', autoRefreshInterval);
    localStorage.setItem('autoRefreshEnabled', autoRefreshEnabled);
    
    alert('设置已保存！');
}

// 加载保存的设置
function loadSettings() {
    const savedInterval = localStorage.getItem('autoRefreshInterval');
    const savedEnabled = localStorage.getItem('autoRefreshEnabled');
    
    if (savedInterval) {
        autoRefreshInterval = parseFloat(savedInterval);
        document.getElementById('autoRefreshInterval').value = autoRefreshInterval;
        document.getElementById('refreshInterval').textContent = autoRefreshInterval;
        document.getElementById('currentInterval').textContent = autoRefreshInterval + '秒';
    }
    
    if (savedEnabled !== null) {
        autoRefreshEnabled = savedEnabled === 'true';
        document.getElementById('enableAutoRefresh').checked = autoRefreshEnabled;
        if (!autoRefreshEnabled) {
            document.getElementById('toggleRefreshBtn').innerHTML = '▶️ 继续';
            document.getElementById('refreshStatus').textContent = '已暂停';
            document.getElementById('refreshStatus').style.color = '#f44336';
            document.getElementById('currentStatus').textContent = '已禁用';
            document.getElementById('currentStatus').style.color = '#f44336';
        }
    }
}

// 更新最后刷新时间
function updateLastRefreshTime() {
    const now = new Date();
    const timeStr = now.toLocaleTimeString('zh-CN');
    document.getElementById('lastRefreshTime').textContent = timeStr;
}

// AI模型配置管理
class AIConfigManager {
    constructor() {
        this.configs = [];
        this.apiBase = window.location.origin;
    }

    // 初始化
    async init() {
        await this.loadConfigs();
        this.renderConfigList();
    }

    // 加载所有配置
    async loadConfigs() {
        try {
            const response = await fetch(`${this.apiBase}/api/aimodelconfig`);
            if (response.ok) {
                this.configs = await response.json();
            } else {
                console.error('加载AI模型配置失败:', await response.text());
            }
        } catch (error) {
            console.error('加载AI模型配置失败:', error);
        }
    }

    // 渲染配置列表
    renderConfigList() {
        const container = document.getElementById('aiConfigList');
        if (!container) return;

        if (this.configs.length === 0) {
            container.innerHTML = '<p class="no-data">暂无AI模型配置，请添加配置</p>';
            return;
        }

        let html = `
            <table>
                <thead>
                    <tr>
                        <th>名称</th>
                        <th>模型名称</th>
                        <th>订阅端点</th>
                        <th>状态</th>
                        <th>默认</th>
                        <th>操作</th>
                    </tr>
                </thead>
                <tbody>
        `;

        this.configs.forEach(config => {
            html += `
                <tr>
                    <td>${config.name}</td>
                    <td>${config.modelName || '-'}</td>
                    <td>${config.subscribeEndpoint}</td>
                    <td>
                        <span class="${config.isActive ? 'status-active' : 'status-inactive'}">
                            ${config.isActive ? '激活' : '未激活'}
                        </span>
                    </td>
                    <td>
                        ${config.isDefault ? '✓' : ''}
                    </td>
                    <td>
                        <button class="btn btn-small" onclick="aiConfigManager.editConfig(${config.id})">编辑</button>
                        <button class="btn btn-danger btn-small" onclick="aiConfigManager.deleteConfig(${config.id})">删除</button>
                        <button class="btn btn-small" onclick="aiConfigManager.testConfig(${config.id})">测试</button>
                    </td>
                </tr>
            `;
        });

        html += `
                </tbody>
            </table>
        `;

        container.innerHTML = html;
    }

    // 显示创建配置表单
    showCreateForm() {
        this.showConfigForm({
            id: 0,
            name: '',
            apiKey: '',
            subscribeEndpoint: '',
            modelName: '',
            isActive: false,
            isDefault: false
        });
    }

    // 显示编辑配置表单
    showConfigForm(config) {
        const form = document.getElementById('aiConfigForm');
        if (!form) return;

        document.getElementById('configId').value = config.id;
        document.getElementById('configName').value = config.name;
        document.getElementById('configApiKey').value = config.apiKey;
        document.getElementById('configSubscribeEndpoint').value = config.subscribeEndpoint;
        document.getElementById('configModelName').value = config.modelName || '';
        document.getElementById('configIsActive').checked = config.isActive;
        document.getElementById('configIsDefault').checked = config.isDefault;

        // 设置表单标题
        document.getElementById('formTitle').textContent = config.id === 0 ? '添加新配置' : '编辑配置';
        
        form.classList.remove('hidden');
    }

    // 保存配置
    async saveConfig() {
        const config = {
            id: parseInt(document.getElementById('configId').value),
            name: document.getElementById('configName').value.trim(),
            apiKey: document.getElementById('configApiKey').value.trim(),
            subscribeEndpoint: document.getElementById('configSubscribeEndpoint').value.trim(),
            modelName: document.getElementById('configModelName').value.trim(),
            isActive: document.getElementById('configIsActive').checked,
            isDefault: document.getElementById('configIsDefault').checked
        };

        if (!config.name || !config.apiKey || !config.subscribeEndpoint) {
            alert('请填写必填字段');
            return;
        }

        try {
            let response;
            if (config.id === 0) {
                // 创建新配置
                response = await fetch(`${this.apiBase}/api/aimodelconfig`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(config)
                });
            } else {
                // 更新配置
                response = await fetch(`${this.apiBase}/api/aimodelconfig/${config.id}`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(config)
                });
            }

            if (response.ok) {
                this.hideConfigForm();
                await this.loadConfigs();
                this.renderConfigList();
                alert('保存成功！');
            } else {
                alert('保存失败：' + await response.text());
            }
        } catch (error) {
            alert('保存失败：' + error.message);
        }
    }

    // 编辑配置
    async editConfig(id) {
        const config = this.configs.find(c => c.id === id);
        if (config) {
            this.showConfigForm(config);
        }
    }

    // 删除配置
    async deleteConfig(id) {
        if (!confirm('确定要删除这个配置吗？')) {
            return;
        }

        try {
            const response = await fetch(`${this.apiBase}/api/aimodelconfig/${id}`, {
                method: 'DELETE'
            });

            if (response.ok) {
                await this.loadConfigs();
                this.renderConfigList();
                alert('删除成功！');
            } else {
                alert('删除失败：' + await response.text());
            }
        } catch (error) {
            alert('删除失败：' + error.message);
        }
    }

    // 测试配置连接
    async testConfig(id) {
        const config = this.configs.find(c => c.id === id);
        if (!config) return;

        // 构造测试请求对象，只包含必要字段
        const testRequest = {
            apiKey: config.apiKey,
            subscribeEndpoint: config.subscribeEndpoint,
            modelName: config.modelName
        };

        try {
            const response = await fetch(`${this.apiBase}/api/aimodelconfig/test`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(testRequest)
            });

            if (response.ok) {
                alert('连接测试成功！');
            } else {
                alert('连接测试失败：' + await response.text());
            }
        } catch (error) {
            alert('连接测试失败：' + error.message);
        }
    }

    // 取消编辑
    cancelEdit() {
        this.hideConfigForm();
    }

    // 隐藏配置表单
    hideConfigForm() {
        const form = document.getElementById('aiConfigForm');
        if (form) {
            form.classList.add('hidden');
        }
    }

    // 测试连接
    async testConnection() {
        const config = {
            apiKey: document.getElementById('configApiKey').value.trim(),
            subscribeEndpoint: document.getElementById('configSubscribeEndpoint').value.trim(),
            modelName: document.getElementById('configModelName').value.trim()
        };

        if (!config.apiKey || !config.subscribeEndpoint || !config.modelName) {
            alert('请填写API Key、订阅端点和模型名称');
            return;
        }

        try {
            const response = await fetch(`${this.apiBase}/api/aimodelconfig/test`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(config)
            });

            if (response.ok) {
                alert('连接测试成功！');
            } else {
                alert('连接测试失败：' + await response.text());
            }
        } catch (error) {
            alert('连接测试失败：' + error.message);
        }
    }
}

// 初始化AI配置管理器
const aiConfigManager = new AIConfigManager();

// 提示词管理
class AIPromptManager {
    constructor() {
        this.prompts = [];
        this.apiBase = window.location.origin;
    }

    async init() {
        await this.loadPrompts();
        this.renderPromptList();
        // 初始化AI分析下拉
        this.fillPromptSelect();
    }

    async loadPrompts() {
        try {
            const res = await fetch(`${this.apiBase}/api/aiprompts`);
            this.prompts = res.ok ? await res.json() : [];
            const container = document.getElementById('aiPromptList');
            if (!container) return;
            this.renderPromptList();
        } catch (e) {
            console.error('加载提示词失败:', e);
        }
    }

    renderPromptList() {
        const container = document.getElementById('aiPromptList');
        if (!container) return;
        if (!this.prompts || this.prompts.length === 0) {
            container.innerHTML = '<p class="no-data">暂无提示词，请添加</p>';
            return;
        }
        let html = `
            <table>
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
        `;
        this.prompts.forEach(p => {
            html += `
                <tr>
                    <td>${p.name}</td>
                    <td>${p.temperature}</td>
                    <td>${p.isDefault ? '✓' : ''}</td>
                    <td>${p.isActive ? '✓' : ''}</td>
                    <td>
                        <button class="btn btn-small" onclick="aiPromptManager.editPrompt(${p.id})">编辑</button>
                        <button class="btn btn-danger btn-small" onclick="aiPromptManager.deletePrompt(${p.id})">删除</button>
                    </td>
                </tr>
            `;
        });
        html += `</tbody></table>`;
        container.innerHTML = html;
    }

    showCreateForm() {
        this.showPromptForm({
            id: 0, name: '', systemPrompt: '', temperature: 0.7, isDefault: false, isActive: true
        });
    }

    showPromptForm(p) {
        document.getElementById('promptId').value = p.id;
        document.getElementById('promptName').value = p.name;
        document.getElementById('promptText').value = p.systemPrompt;
        document.getElementById('promptTemp').value = p.temperature;
        document.getElementById('promptIsDefault').checked = !!p.isDefault;
        document.getElementById('promptIsActive').checked = !!p.isActive;
        document.getElementById('promptFormTitle').textContent = p.id === 0 ? '添加新提示词' : '编辑提示词';
        document.getElementById('aiPromptForm').classList.remove('hidden');
    }

    async savePrompt() {
        const payload = {
            id: parseInt(document.getElementById('promptId').value),
            name: document.getElementById('promptName').value.trim(),
            systemPrompt: document.getElementById('promptText').value.trim(),
            temperature: parseFloat(document.getElementById('promptTemp').value),
            isDefault: document.getElementById('promptIsDefault').checked,
            isActive: document.getElementById('promptIsActive').checked
        };
        if (!payload.name || !payload.systemPrompt) {
            alert('请填写名称和系统提示词');
            return;
        }
        try {
            let res;
            if (payload.id === 0) {
                res = await fetch(`${this.apiBase}/api/aiprompts`, {
                    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload)
                });
            } else {
                res = await fetch(`${this.apiBase}/api/aiprompts/${payload.id}`, {
                    method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload)
                });
            }
            if (res.ok) {
                await this.loadPrompts();
                this.fillPromptSelect();
                this.hidePromptForm();
                alert('保存成功！');
            } else {
                alert('保存失败：' + await res.text());
            }
        } catch (e) {
            alert('保存失败：' + e.message);
        }
    }

    async editPrompt(id) {
        const p = this.prompts.find(x => x.id === id);
        if (p) this.showPromptForm(p);
    }

    async deletePrompt(id) {
        if (!confirm('确定要删除这个提示词吗？')) return;
        try {
            const res = await fetch(`${this.apiBase}/api/aiprompts/${id}`, { method: 'DELETE' });
            if (res.ok) {
                await this.loadPrompts();
                this.fillPromptSelect();
                alert('删除成功！');
            } else {
                alert('删除失败：' + await res.text());
            }
        } catch (e) {
            alert('删除失败：' + e.message);
        }
    }

    cancelEdit() { this.hidePromptForm(); }
    hidePromptForm() { document.getElementById('aiPromptForm').classList.add('hidden'); }

    // 填充AI分析下拉
    fillPromptSelect() {
        const sel = document.getElementById('aiPromptSelect');
        if (!sel) return;
        let html = '<option value="">（使用默认或JSON配置）</option>';
        this.prompts.filter(p => p.isActive).forEach(p => {
            html += `<option value="${p.id}" ${p.isDefault ? 'selected' : ''}>${p.name}</option>`;
        });
        sel.innerHTML = html;
    }
}

// 初始化管理器
const aiPromptManager = new AIPromptManager();

// 选股模板管理
class ScreenTemplateManager {
    constructor() {
        this.templates = [];
        // 与其他API调用保持一致，使用统一的API基地址
        this.apiBase = API_BASE;
        this.currentEditingId = null;
    }

    async init() {
        await this.loadTemplates();
        this.renderTemplateSelect();
    }

    async loadTemplates() {
        try {
            // 修正路径以匹配后端控制器 ScreenTemplateController 的路由
            const res = await fetch(`${this.apiBase}/api/ScreenTemplate`);
            this.templates = res.ok ? await res.json() : [];
            this.renderTemplateSelect();
        } catch (e) {
            console.error('加载选股模板失败:', e);
        }
    }

    renderTemplateSelect() {
        const select = document.getElementById('templateSelect');
        if (!select) return;
        
        let html = '<option value="">选择模板...</option>';
        this.templates.forEach(template => {
            html += `<option value="${template.id}" ${template.isDefault ? 'selected' : ''}>${template.name}</option>`;
        });
        select.innerHTML = html;
    }

    async loadTemplate() {
        const templateId = document.getElementById('templateSelect').value;
        if (!templateId) return;

        try {
            // 修正路径以匹配后端控制器 ScreenTemplateController 的路由
            const res = await fetch(`${this.apiBase}/api/ScreenTemplate/${templateId}`);
            if (!res.ok) throw new Error('加载模板失败');
            
            const template = await res.json();
            this.fillFormWithTemplate(template);
        } catch (e) {
            alert('加载模板失败：' + e.message);
        }
    }

    fillFormWithTemplate(template) {
        // 填充表单字段
        document.getElementById('market').value = template.market || '';
        document.getElementById('minPrice').value = template.minPrice || '';
        document.getElementById('maxPrice').value = template.maxPrice || '';
        document.getElementById('minChange').value = template.minChangePercent || '';
        document.getElementById('maxChange').value = template.maxChangePercent || '';
        document.getElementById('minTurnover').value = template.minTurnoverRate || '';
        document.getElementById('maxTurnover').value = template.maxTurnoverRate || '';
        document.getElementById('minVolume').value = template.minVolume || '';
        document.getElementById('maxVolume').value = template.maxVolume || '';
        document.getElementById('minMarketValue').value = template.minMarketValue || '';
        document.getElementById('maxMarketValue').value = template.maxMarketValue || '';
        document.getElementById('minDividendYield').value = template.minDividendYield || '';
        document.getElementById('maxDividendYield').value = template.maxDividendYield || '';
    }

    showSaveDialog() {
        document.getElementById('saveTemplateTitle').textContent = '保存选股模板';
        document.getElementById('templateName').value = '';
        document.getElementById('templateDescription').value = '';
        document.getElementById('setAsDefault').checked = false;
        this.currentEditingId = null;
        document.getElementById('saveTemplateModal').style.display = 'block';
    }

    showEditDialog() {
        const templateId = document.getElementById('templateSelect').value;
        if (!templateId) {
            alert('请先选择一个模板');
            return;
        }

        const template = this.templates.find(t => t.id == templateId);
        if (!template) return;

        document.getElementById('saveTemplateTitle').textContent = '编辑选股模板';
        document.getElementById('templateName').value = template.name;
        document.getElementById('templateDescription').value = template.description || '';
        document.getElementById('setAsDefault').checked = template.isDefault;
        this.currentEditingId = templateId;
        document.getElementById('saveTemplateModal').style.display = 'block';
    }

    async saveTemplate() {
        const name = document.getElementById('templateName').value.trim();
        if (!name) {
            alert('请输入模板名称');
            return;
        }

        const templateData = {
            name: name,
            description: document.getElementById('templateDescription').value.trim(),
            market: document.getElementById('market').value || null,
            minPrice: parseFloat(document.getElementById('minPrice').value) || null,
            maxPrice: parseFloat(document.getElementById('maxPrice').value) || null,
            minChangePercent: parseFloat(document.getElementById('minChange').value) || null,
            maxChangePercent: parseFloat(document.getElementById('maxChange').value) || null,
            minTurnoverRate: parseFloat(document.getElementById('minTurnover').value) || null,
            maxTurnoverRate: parseFloat(document.getElementById('maxTurnover').value) || null,
            minVolume: parseFloat(document.getElementById('minVolume').value) || null,
            maxVolume: parseFloat(document.getElementById('maxVolume').value) || null,
            minMarketValue: parseFloat(document.getElementById('minMarketValue').value) || null,
            maxMarketValue: parseFloat(document.getElementById('maxMarketValue').value) || null,
            minDividendYield: parseFloat(document.getElementById('minDividendYield').value) || null,
            maxDividendYield: parseFloat(document.getElementById('maxDividendYield').value) || null,
            isDefault: document.getElementById('setAsDefault').checked
        };

        try {
            let response;
            if (this.currentEditingId) {
                // 更新模板
                response = await fetch(`${this.apiBase}/api/screentemplates/${this.currentEditingId}`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(templateData)
                });
            } else {
                // 创建新模板
                response = await fetch(`${this.apiBase}/api/screentemplates`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(templateData)
                });
            }

            if (response.ok) {
                alert(this.currentEditingId ? '模板更新成功！' : '模板保存成功！');
                this.hideSaveDialog();
                await this.loadTemplates();
            } else {
                throw new Error(await response.text());
            }
        } catch (e) {
            alert('保存失败：' + e.message);
        }
    }

    async deleteTemplate() {
        const templateId = document.getElementById('templateSelect').value;
        if (!templateId) {
            alert('请先选择一个模板');
            return;
        }

        const template = this.templates.find(t => t.id == templateId);
        if (!template) return;

        if (!confirm(`确定要删除模板"${template.name}"吗？`)) return;

        try {
            const response = await fetch(`${this.apiBase}/api/screentemplates/${templateId}`, {
                method: 'DELETE'
            });

            if (response.ok) {
                alert('模板删除成功！');
                await this.loadTemplates();
                this.clearForm();
            } else {
                throw new Error(await response.text());
            }
        } catch (e) {
            alert('删除失败：' + e.message);
        }
    }

    clearForm() {
        document.getElementById('templateSelect').value = '';
        document.getElementById('market').value = '';
        document.getElementById('minPrice').value = '';
        document.getElementById('maxPrice').value = '';
        document.getElementById('minChange').value = '';
        document.getElementById('maxChange').value = '';
        document.getElementById('minTurnover').value = '';
        document.getElementById('maxTurnover').value = '';
        document.getElementById('minVolume').value = '';
        document.getElementById('maxVolume').value = '';
        document.getElementById('minMarketValue').value = '';
        document.getElementById('maxMarketValue').value = '';
        document.getElementById('minDividendYield').value = '';
        document.getElementById('maxDividendYield').value = '';
    }

    hideSaveDialog() {
        document.getElementById('saveTemplateModal').style.display = 'none';
    }
}

// 初始化选股模板管理器
const screenTemplateManager = new ScreenTemplateManager();

// 全局函数供HTML调用
function loadTemplate() { screenTemplateManager.loadTemplate(); }
function showSaveTemplateDialog() { screenTemplateManager.showSaveDialog(); }
function showEditTemplateDialog() { screenTemplateManager.showEditDialog(); }
function saveTemplate() { screenTemplateManager.saveTemplate(); }
function deleteTemplate() { screenTemplateManager.deleteTemplate(); }
function clearConditions() { screenTemplateManager.clearForm(); }
function hideSaveTemplateDialog() { screenTemplateManager.hideSaveDialog(); }

// 量化交易管理
const quantTrading = {
    // 加载策略列表
    async loadStrategies() {
        try {
            // 修复：从量化交易控制器读取数据库中的策略
            const response = await fetch(`${API_BASE}/api/QuantTrading/strategies`);
            const strategies = await response.json();

            // 缓存以便后续根据名称查找ID
            this._strategies = Array.isArray(strategies) ? strategies : [];

            const strategyList = document.getElementById('strategyList');
            const strategySelect = document.getElementById('strategySelect');
            
            if (!Array.isArray(strategies) || strategies.length === 0) {
                strategyList.innerHTML = '<p class="no-data">暂无策略，请先导入默认策略</p>';
                strategySelect.innerHTML = '<option value="">暂无策略</option>';
                return;
            }
            
            // 名称中文映射（仅显示，用于前端友好展示）
            const translateStrategyName = (name) => {
                if (!name) return '';
                const n = String(name).toLowerCase();
                if (n === 'ma-cross-strategy') return '简单移动平均策略';
                if (n === 'macd-strategy') return 'MACD指标策略';
                if (n === 'rsi-strategy') return 'RSI指标策略';
                return name; // 其他保持原样
            };

            // 防御式资金显示：优先currentCapital，回退initialCapital，格式化失败显示-
            strategyList.innerHTML = strategies.map(strategy => {
                const cap = typeof strategy.currentCapital === 'number'
                    ? strategy.currentCapital
                    : (typeof strategy.initialCapital === 'number' ? strategy.initialCapital : undefined);
                const capStr = typeof cap === 'number' ? `¥${cap.toLocaleString()}` : '¥-';

                return `
                    <div class="stock-card" style="margin-bottom: 15px;">
                        <div class="stock-header">
                            <div class="stock-name-section">
                                <div class="stock-name">${translateStrategyName(strategy.name)}</div>
                                <div class="stock-code">${strategy.type} | 资金: ${capStr}</div>
                            </div>
                            <div class="stock-actions">
                                <button class="btn btn-small ${strategy.isActive ? 'btn-success' : ''}" 
                                        onclick="quantTrading.toggleStrategy('${strategy.id}', ${!strategy.isActive})">
                                    ${strategy.isActive ? '✅ 已启用' : '⏸️ 已停用'}
                                </button>
                                <button class="btn btn-danger btn-small" 
                                        onclick="quantTrading.deleteStrategyById(${strategy.id}, '${translateStrategyName(strategy.name).replace(/'/g, "\\'")}')">🗑️ 删除</button>
                            </div>
                        </div>
                        <div class="price-section">
                            <div style="font-size: 0.9em; color: #666;">${strategy.description || '暂无描述'}</div>
                            <div class="price-info-row" style="margin-top: 10px;">
                                <div class="price-item">
                                    <span class="price-label">初始资金</span>
                                    <span class="price-value">¥${(typeof strategy.initialCapital === 'number' ? strategy.initialCapital.toLocaleString() : '-')}</span>
                                </div>
                            </div>
                        </div>
                    </div>
                `;
            }).join('');

            // 下拉框按英文值传递，中文显示，保证后端文件查找不受影响
            strategySelect.innerHTML = '<option value="">请选择策略...</option>' + 
                strategies.map(s => `<option value="${s.name}">${translateStrategyName(s.name)} (${s.type})</option>`).join('');
            
            // 同步填充策略优化下拉框
            const optimizationStrategySelect = document.getElementById('optimizationStrategySelect');
            if (optimizationStrategySelect) {
                optimizationStrategySelect.innerHTML = '<option value="">请选择要优化的策略...</option>' + 
                    strategies.map(s => `<option value="${s.name}">${translateStrategyName(s.name)} (${s.type})</option>`).join('');
            }
            
            // 回测股票选择下拉框已在loadWatchlist中自动填充
                
        } catch (error) {
            console.error('加载策略失败:', error);
            document.getElementById('strategyList').innerHTML = '<p class="error">加载策略失败</p>';
        }
    },

    // 导入默认策略
    async importDefaultStrategies() {
        try {
            const response = await fetch(`${API_BASE}/api/StrategyConfig/import`, {
                method: 'POST'
            });

            const contentType = response.headers.get('content-type') || '';
            const isJson = contentType.includes('application/json');
            const result = isJson ? await response.json() : await response.text();

            if (response.ok) {
                const imported = isJson ? (result.importedCount ?? result.count ?? 0) : 0;
                alert(`成功导入 ${imported} 个策略`);
                await this.loadStrategies();
            } else {
                const msg = isJson ? (result.message || '导入失败') : result;
                alert('导入失败: ' + msg);
            }
        } catch (error) {
            console.error('导入策略失败:', error);
            alert('导入策略失败');
        }
    },

    // 加载策略配置
    async loadStrategyConfig() {
        const strategyName = document.getElementById('strategySelect').value;
        const configForm = document.getElementById('strategyConfigForm');
        
        if (!strategyName) {
            configForm.classList.add('hidden');
            return;
        }
        
        try {
            const response = await fetch(`${API_BASE}/api/StrategyConfig/configs/${encodeURIComponent(strategyName)}`);
            const config = await response.json();
            
            if (response.ok) {
                // 填充表单
                document.getElementById('strategyName').value = config.name;
                document.getElementById('strategyDescription').value = config.description || '';
                document.getElementById('initialCapital').value = config.initialCapital;
                document.getElementById('strategyType').value = config.type;
                document.getElementById('isActive').checked = config.isActive;
                
                // 加载策略参数
                this.loadStrategyParameters(config.type, config.parameters);
                
                configForm.classList.remove('hidden');
            } else {
                alert('加载策略配置失败');
            }
        } catch (error) {
            console.error('加载策略配置失败:', error);
            alert('加载策略配置失败');
        }
    },

    // 加载策略参数表单
    loadStrategyParameters(strategyType, parameters = {}) {
        const container = document.getElementById('strategyParameters');
        let html = '<h4>策略参数</h4>';
        
        switch (strategyType) {
            case 'TechnicalIndicator':
                html += `
                    <div class="form-group">
                        <label>短期均线周期</label>
                        <input type="number" id="shortPeriod" value="${parameters.shortPeriod || 5}" min="1">
                    </div>
                    <div class="form-group">
                        <label>长期均线周期</label>
                        <input type="number" id="longPeriod" value="${parameters.longPeriod || 20}" min="1">
                    </div>
                    <div class="form-group">
                        <label>快速EMA周期</label>
                        <input type="number" id="fastPeriod" value="${parameters.fastPeriod || 12}" min="1">
                    </div>
                    <div class="form-group">
                        <label>慢速EMA周期</label>
                        <input type="number" id="slowPeriod" value="${parameters.slowPeriod || 26}" min="1">
                    </div>
                    <div class="form-group">
                        <label>信号线周期</label>
                        <input type="number" id="signalPeriod" value="${parameters.signalPeriod || 9}" min="1">
                    </div>
                    <div class="form-group">
                        <label>RSI周期</label>
                        <input type="number" id="rsiPeriod" value="${parameters.rsiPeriod || 14}" min="1">
                    </div>
                    <div class="form-group">
                        <label>RSI超买阈值</label>
                        <input type="number" id="rsiOverBought" value="${parameters.rsiOverBought || 70}" min="50" max="100">
                    </div>
                    <div class="form-group">
                        <label>RSI超卖阈值</label>
                        <input type="number" id="rsiOverSold" value="${parameters.rsiOverSold || 30}" min="0" max="50">
                    </div>
                    <div class="form-group">
                        <label>布林带周期</label>
                        <input type="number" id="bollingerPeriod" value="${parameters.bollingerPeriod || 20}" min="1">
                    </div>
                    <div class="form-group">
                        <label>布林带标准差</label>
                        <input type="number" id="bollingerStdDev" value="${parameters.bollingerStdDev || 2.0}" min="0.1" step="0.1">
                    </div>
                `;
                break;
            case 'Fundamental':
                html += `
                    <div class="form-group">
                        <label>PE比率阈值</label>
                        <input type="number" id="peRatio" value="${parameters.peRatio || 15}" min="1">
                    </div>
                    <div class="form-group">
                        <label>PB比率阈值</label>
                        <input type="number" id="pbRatio" value="${parameters.pbRatio || 2}" min="0.1" step="0.1">
                    </div>
                `;
                break;
            case 'MA_CROSS':
                html += `
                    <div class="form-group">
                        <label>短期均线周期</label>
                        <input type="number" id="shortPeriod" value="${parameters.shortPeriod || 5}" min="1">
                    </div>
                    <div class="form-group">
                        <label>长期均线周期</label>
                        <input type="number" id="longPeriod" value="${parameters.longPeriod || 20}" min="1">
                    </div>
                `;
                break;
            case 'RSI':
                html += `
                    <div class="form-group">
                        <label>RSI周期</label>
                        <input type="number" id="rsiPeriod" value="${parameters.rsiPeriod || 14}" min="1">
                    </div>
                    <div class="form-group">
                        <label>超买阈值</label>
                        <input type="number" id="overboughtThreshold" value="${parameters.overboughtThreshold || 70}" min="50" max="100">
                    </div>
                    <div class="form-group">
                        <label>超卖阈值</label>
                        <input type="number" id="oversoldThreshold" value="${parameters.oversoldThreshold || 30}" min="0" max="50">
                    </div>
                `;
                break;
            case 'BOLLINGER':
                html += `
                    <div class="form-group">
                        <label>布林带周期</label>
                        <input type="number" id="bollingerPeriod" value="${parameters.bollingerPeriod || 20}" min="1">
                    </div>
                    <div class="form-group">
                        <label>标准差倍数</label>
                        <input type="number" id="standardDeviation" value="${parameters.standardDeviation || 2}" min="0.1" step="0.1">
                    </div>
                `;
                break;
        }
        
        container.innerHTML = html;
    },

    // 保存策略
    async saveStrategy() {
        const strategyName = document.getElementById('strategyName').value.trim();
        const strategyType = document.getElementById('strategyType').value;
        
        if (!strategyName) {
            alert('请输入策略名称');
            return;
        }
        
        // 收集策略参数
        const parameters = this.collectStrategyParameters(strategyType);
        
        const config = {
            name: strategyName,
            description: document.getElementById('strategyDescription').value,
            type: strategyType,
            parameters: parameters,
            initialCapital: parseFloat(document.getElementById('initialCapital').value),
            isActive: document.getElementById('isActive').checked
        };
        
        try {
            const response = await fetch(`${API_BASE}/api/StrategyConfig/configs`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(config)
            });
            
            if (response.ok) {
                alert('策略保存成功');
                this.loadStrategies();
            } else {
                const error = await response.text();
                alert('保存失败: ' + error);
            }
        } catch (error) {
            console.error('保存策略失败:', error);
            alert('保存策略失败');
        }
    },

    // 收集策略参数
    collectStrategyParameters(strategyType) {
        const parameters = {};
        
        switch (strategyType) {
            case 'TechnicalIndicator':
                parameters.shortPeriod = parseInt(document.getElementById('shortPeriod').value) || 5;
                parameters.longPeriod = parseInt(document.getElementById('longPeriod').value) || 20;
                parameters.fastPeriod = parseInt(document.getElementById('fastPeriod').value) || 12;
                parameters.slowPeriod = parseInt(document.getElementById('slowPeriod').value) || 26;
                parameters.signalPeriod = parseInt(document.getElementById('signalPeriod').value) || 9;
                parameters.rsiPeriod = parseInt(document.getElementById('rsiPeriod').value) || 14;
                parameters.overboughtThreshold = parseFloat(document.getElementById('rsiOverBought').value) || 70;
                parameters.oversoldThreshold = parseFloat(document.getElementById('rsiOverSold').value) || 30;
                parameters.bollingerPeriod = parseInt(document.getElementById('bollingerPeriod').value) || 20;
                parameters.standardDeviation = parseFloat(document.getElementById('bollingerStdDev').value) || 2.0;
                break;
            case 'Fundamental':
                parameters.peRatio = parseFloat(document.getElementById('peRatio').value) || 15;
                parameters.pbRatio = parseFloat(document.getElementById('pbRatio').value) || 2;
                break;
            case 'MA_CROSS':
                parameters.shortPeriod = parseInt(document.getElementById('shortPeriod').value);
                parameters.longPeriod = parseInt(document.getElementById('longPeriod').value);
                break;
            case 'RSI':
                parameters.rsiPeriod = parseInt(document.getElementById('rsiPeriod').value);
                parameters.overboughtThreshold = parseFloat(document.getElementById('overboughtThreshold').value);
                parameters.oversoldThreshold = parseFloat(document.getElementById('oversoldThreshold').value);
                break;
            case 'BOLLINGER':
                parameters.bollingerPeriod = parseInt(document.getElementById('bollingerPeriod').value);
                parameters.standardDeviation = parseFloat(document.getElementById('standardDeviation').value);
                break;
        }
        
        return parameters;
    },

    // 运行回测
    async runBacktest() {
        const stockCode = document.getElementById('backtestStock').value.trim();
        const startDate = document.getElementById('backtestStartDate').value;
        const endDate = document.getElementById('backtestEndDate').value;
        const strategyName = document.getElementById('strategySelect').value;
        
        if (!stockCode || !startDate || !endDate || !strategyName) {
            alert('请填写完整的回测参数');
            return;
        }
        
        try {
            const response = await fetch(`${API_BASE}/api/Backtest/run`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    stockCode: stockCode,
                    strategyName: strategyName,
                    startDate: startDate,
                    endDate: endDate
                })
            });
            
            const result = await response.json();
            
            if (response.ok) {
                this.displayBacktestResults(result);
            } else {
                alert('回测失败: ' + result.message);
            }
        } catch (error) {
            console.error('回测失败:', error);
            alert('回测失败');
        }
    },

    // 显示回测结果
    displayBacktestResults(result) {
        document.getElementById('totalReturn').textContent = (result.totalReturn * 100).toFixed(2) + '%';
        document.getElementById('annualReturn').textContent = (result.annualizedReturn * 100).toFixed(2) + '%';
        document.getElementById('maxDrawdown').textContent = (result.maxDrawdown * 100).toFixed(2) + '%';
        document.getElementById('sharpeRatio').textContent = result.sharpeRatio.toFixed(2);
        document.getElementById('tradeCount').textContent = result.totalTrades;
        document.getElementById('winRate').textContent = (result.winRate * 100).toFixed(1) + '%';
        
        // 显示交易记录
        const tradeHistory = document.getElementById('tradeHistory');
        if (result.trades && result.trades.length > 0) {
            tradeHistory.innerHTML = `
                <table>
                    <thead>
                        <tr>
                            <th>日期</th>
                            <th>类型</th>
                            <th>价格</th>
                            <th>数量</th>
                            <th>金额</th>
                            <th>收益</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${result.trades.map(trade => `
                            <tr>
                                <td>${new Date(trade.executedAt).toLocaleDateString()}</td>
                                <td style="color: ${trade.type === 'BUY' ? '#f44336' : '#4caf50'}">${trade.type === 'BUY' ? '买入' : '卖出'}</td>
                                <td>¥${trade.price.toFixed(2)}</td>
                                <td>${trade.quantity}</td>
                                <td>¥${(trade.price * trade.quantity).toFixed(2)}</td>
                                <td style="color: ${trade.profit >= 0 ? '#f44336' : '#4caf50'}">${trade.profit ? trade.profit.toFixed(2) + '%' : '-'}</td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>
            `;
        } else {
            tradeHistory.innerHTML = '<p class="no-data">暂无交易记录</p>';
        }
        
        document.getElementById('backtestResults').classList.remove('hidden');
    },

    // 切换策略状态
    async toggleStrategy(strategyId, isActive) {
        try {
            const response = await fetch(`${API_BASE}/api/QuantTrading/strategies/${strategyId}/toggle`, {
                method: 'POST'
            });
            
            if (response.ok) {
                this.loadStrategies();
            } else {
                alert('切换策略状态失败');
            }
        } catch (error) {
            console.error('切换策略状态失败:', error);
            alert('切换策略状态失败');
        }
    },

    // 显示创建策略表单
    showCreateStrategy() {
        document.getElementById('strategySelect').value = '';
        document.getElementById('strategyName').value = '';
        document.getElementById('strategyDescription').value = '';
        document.getElementById('initialCapital').value = '100000';
        document.getElementById('strategyType').value = 'MA_CROSS';
        document.getElementById('isActive').checked = true;
        
        this.loadStrategyParameters('MA_CROSS');
        document.getElementById('strategyConfigForm').classList.remove('hidden');
    },

    // 删除策略（按选择的策略名称 -> 映射到ID再删除）
    async deleteStrategy() {
        const strategyName = document.getElementById('strategySelect').value;
        if (!strategyName) {
            alert('请先选择要删除的策略');
            return;
        }

        const match = (this._strategies || []).find(s => s.name === strategyName);
        if (!match) {
            alert('未找到该策略，请先刷新策略列表');
            return;
        }

        if (!confirm(`确定要删除策略 "${strategyName}" 吗？`)) {
            return;
        }

        try {
            const response = await fetch(`${API_BASE}/api/QuantTrading/strategies/${match.id}`, {
                method: 'DELETE'
            });
            if (response.ok) {
                alert('策略删除成功');
                document.getElementById('strategyConfigForm').classList.add('hidden');
                this.loadStrategies();
            } else {
                const txt = await response.text();
                alert('删除策略失败：' + txt);
            }
        } catch (error) {
            console.error('删除策略失败:', error);
            alert('删除策略失败');
        }
    },

    // 新增：按ID删除（卡片上的删除按钮调用）
    async deleteStrategyById(id, name) {
        if (!id) {
            alert('策略ID缺失');
            return;
        }
        if (!confirm(`确定要删除策略 "${name}" 吗？`)) {
            return;
        }
        try {
            const response = await fetch(`${API_BASE}/api/QuantTrading/strategies/${id}`, {
                method: 'DELETE'
            });
            if (response.ok) {
                alert('策略删除成功');
                this.loadStrategies();
            } else {
                const txt = await response.text();
                alert('删除策略失败：' + txt);
            }
        } catch (error) {
            console.error('删除策略失败:', error);
            alert('删除策略失败');
        }
    },

    // 测试策略
    async testStrategy() {
        alert('策略测试功能开发中...');
    },

    // 开始监控
    startMonitoring() {
        document.getElementById('monitoringStatus').innerHTML = '<span style="color: #28a745;">✅ 监控已启动</span>';
        alert('实时监控功能开发中...');
    },

    // 停止监控
    stopMonitoring() {
        document.getElementById('monitoringStatus').innerHTML = '<span style="color: #dc3545;">⏹️ 监控已停止</span>';
    },

    // 加载活跃策略
    async loadActiveStrategies() {
        try {
            const response = await fetch(`${API_BASE}/api/QuantTrading/strategies/active`);
            const strategies = await response.json();
            
            const container = document.getElementById('activeStrategies');
            if (strategies.length === 0) {
                container.innerHTML = '<p class="no-data">暂无活跃策略</p>';
                return;
            }
            
            container.innerHTML = `
                <h4>活跃策略 (${strategies.length})</h4>
                <div class="active-strategies-grid">
                    ${strategies.map(strategy => `
                        <div class="stock-card">
                            <div class="stock-header">
                                <div class="stock-name-section">
                                    <div class="stock-name">${strategy.name}</div>
                                    <div class="stock-code">${strategy.type}</div>
                                </div>
                                <div class="category-label cost-positive">运行中</div>
                            </div>
                            <div class="price-section">
                                <div class="price-info-row">
                                    <div class="price-item">
                                        <span class="price-label">当前资金</span>
                                        <span class="price-value">¥${strategy.currentCapital.toLocaleString()}</span>
                                    </div>
                                    <div class="price-item">
                                        <span class="price-label">收益率</span>
                                        <span class="price-value ${strategy.currentCapital >= strategy.initialCapital ? 'price-up' : 'price-down'}">
                                            ${((strategy.currentCapital - strategy.initialCapital) / strategy.initialCapital * 100).toFixed(2)}%
                                        </span>
                                    </div>
                                </div>
                            </div>
                        </div>
                    `).join('')}
                </div>
            `;
        } catch (error) {
            console.error('加载活跃策略失败:', error);
            document.getElementById('activeStrategies').innerHTML = '<p class="error">加载失败</p>';
        }
    },

    // 新增：当选择下拉框回测股票时，同步到输入框
    onBacktestStockChange() {
        const sel = document.getElementById('backtestStockSelect');
        const input = document.getElementById('backtestStock');
        if (sel && input) {
            input.value = sel.value || '';
        }
    },
    
    // 新增：添加选中的股票到回测列表
    addSelectedStockToBacktest() {
        const sel = document.getElementById('backtestStockSelect');
        const input = document.getElementById('backtestStock');
        if (!sel || !input) return;
        
        const selectedOptions = Array.from(sel.selectedOptions);
        if (!selectedOptions.length) {
            alert('请先选择股票');
            return;
        }
        
        // 获取当前已有的股票代码
        let currentCodes = input.value.split(',')
            .map(code => code.trim())
            .filter(code => code);
        
        // 添加新选中的股票代码
        for (const option of selectedOptions) {
            if (option.value && !currentCodes.includes(option.value)) {
                currentCodes.push(option.value);
            }
        }
        
        // 更新输入框
        input.value = currentCodes.join(',');
    },
    
    // 新增：清空已选股票
    clearBacktestStocks() {
        const input = document.getElementById('backtestStock');
        if (input) {
            input.value = '';
        }
    },
    
    // 新增：运行批量回测
    async runBatchBacktest() {
        const stockCodesInput = document.getElementById('backtestStock').value.trim();
        const startDate = document.getElementById('backtestStartDate').value;
        const endDate = document.getElementById('backtestEndDate').value;
        const strategyName = document.getElementById('strategySelect').value;
        
        if (!stockCodesInput || !startDate || !endDate || !strategyName) {
            alert('请填写完整的回测参数');
            return;
        }
        
        // 解析股票代码列表
        const stockCodes = stockCodesInput.split(',')
            .map(code => code.trim())
            .filter(code => code);
        
        if (!stockCodes.length) {
            alert('请至少选择一只股票');
            return;
        }
        
        try {
            console.log('开始批量回测，参数：', {
                stockCodes,
                strategyName,
                startDate,
                endDate
            });
            
            const response = await fetch(`${API_BASE}/api/Backtest/run-batch`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    stockCodes: stockCodes,
                    strategyName: strategyName,
                    startDate: startDate,
                    endDate: endDate,
                    initialCapital: 100000
                })
            });
            
            if (!response.ok) {
                const errorText = await response.text();
                console.error('批量回测API错误:', response.status, errorText);
                throw new Error(`服务器错误 (${response.status}): ${errorText}`);
            }
            
            const results = await response.json();
            console.log('批量回测结果:', results);
            
            if (results && Array.isArray(results)) {
                this.displayBatchBacktestResults(results);
            } else {
                alert('批量回测返回了无效的结果格式');
            }
        } catch (error) {
            console.error('批量回测失败:', error);
            alert('批量回测失败: ' + error.message);
        }
    },
    
    // 新增：显示批量回测结果
    displayBatchBacktestResults(results) {
        if (!results || !results.length) {
            alert('没有回测结果');
            return;
        }
        
        const summaryList = document.getElementById('backtestSummaryList');
        const detailPanel = document.getElementById('backtestDetailPanel');
        
        // 创建结果摘要列表
        let html = '<h4>批量回测结果</h4><div class="batch-backtest-list">';
        
        results.forEach((result, index) => {
            const returnColor = result.totalReturn >= 0 ? '#f44336' : '#4caf50';
            html += `
                <div class="batch-backtest-item" onclick="quantTrading.showBacktestDetail(${index})">
                    <div class="stock-code">${result.stockCode}</div>
                    <div class="return-rate" style="color: ${returnColor}">
                        ${(result.totalReturn * 100).toFixed(2)}%
                    </div>
                    <div class="trade-count">交易: ${result.totalTrades}次</div>
                    <div class="backtest-actions">
                        <button class="btn btn-small" onclick="event.stopPropagation(); quantTrading.showAIAnalysis(${index})">AI分析</button>
                    </div>
                </div>
            `;
        });
        
        html += '</div>';
        summaryList.innerHTML = html;
        
        // 存储结果数据供详情查看
        this.batchBacktestResults = results;
        
        // 默认显示第一个结果的详情
        if (results.length > 0) {
            setTimeout(() => this.showBacktestDetail(0), 100);
        }
        
        document.getElementById('backtestResults').classList.remove('hidden');
    },
    
    // 新增：显示AI分析对话框
    showAIAnalysis(index) {
        if (!this.batchBacktestResults || !this.batchBacktestResults[index]) return;
        
        const result = this.batchBacktestResults[index];
        const modal = document.getElementById('aiAnalysisModal');
        const loadingDiv = document.getElementById('aiAnalysisLoading');
        const resultDiv = document.getElementById('aiAnalysisResult');
        const contentDiv = resultDiv.querySelector('.analysis-content');
        
        // 显示对话框和加载状态
        modal.style.display = 'block';
        loadingDiv.style.display = 'block';
        resultDiv.classList.add('hidden');
        
        // 设置基本信息
        document.getElementById('aiAnalysisStockCode').textContent = `股票代码: ${result.stockCode}`;
        document.getElementById('aiAnalysisStrategyName').textContent = `策略: ${document.getElementById('strategySelect').value}`;
        document.getElementById('aiAnalysisDateRange').textContent = 
            `时间范围: ${document.getElementById('backtestStartDate').value} 至 ${document.getElementById('backtestEndDate').value}`;
        
        // 构建分析请求的上下文
        const context = `请对这只股票的回测结果进行全面客观的分析，包括：
1. 回测结果数据解读（总收益率、年化收益率、最大回撤、夏普比率、交易次数、胜率等）
2. 该股票的基本面分析（行业、主营业务、市场地位等）
3. 所用策略的适用性分析
4. 投资建议（是否适合持有，适合短期还是长期）
5. 风险提示

回测数据：
- 股票代码: ${result.stockCode}
- 策略名称: ${document.getElementById('strategySelect').value}
- 策略类型: ${document.getElementById('strategyType').value || '未知'}
- 回测时间: ${document.getElementById('backtestStartDate').value} 至 ${document.getElementById('backtestEndDate').value}
- 总收益率: ${(result.totalReturn * 100).toFixed(2)}%
- 年化收益率: ${(result.annualizedReturn * 100).toFixed(2)}%
- 最大回撤: ${(result.maxDrawdown * 100).toFixed(2)}%
- 夏普比率: ${result.sharpeRatio.toFixed(2)}
- 交易次数: ${result.totalTrades}
- 胜率: ${(result.winRate * 100).toFixed(1)}%

请确保分析全面客观，内容详实有深度。`;
        
        // 调用AI分析接口
        this.analyzeBacktestResult(result.stockCode, context)
            .then(analysisResult => {
                loadingDiv.style.display = 'none';
                resultDiv.classList.remove('hidden');
                contentDiv.textContent = analysisResult;
            })
            .catch(error => {
                loadingDiv.style.display = 'none';
                resultDiv.classList.remove('hidden');
                
                // 检查是否是配置相关错误
                if (error.message.includes('请先配置AI模型')) {
                    contentDiv.innerHTML = `
                        <div class="config-error">
                            <h4>需要配置AI模型</h4>
                            <p>您需要先配置AI模型才能使用AI分析功能。请按照以下步骤操作：</p>
                            <ol>
                                <li>点击顶部的"设置"选项卡</li>
                                <li>在"AI模型配置"部分，点击"添加配置"按钮</li>
                                <li>填写您的AI模型信息（如OpenAI、DeepSeek等）</li>
                                <li>保存配置并设置为"激活"状态</li>
                                <li>返回此页面重新尝试分析</li>
                            </ol>
                            <button class="btn" onclick="switchTab('settings'); document.getElementById('aiAnalysisModal').style.display='none';">
                                前往设置页面
                            </button>
                        </div>
                    `;
                } else {
                    contentDiv.textContent = '分析失败: ' + error.message;
                }
            });
    },
    
    // 新增：调用AI分析接口
    async analyzeBacktestResult(stockCode, context) {
        try {
            // 使用用户选择的提示词ID，如果没有选择则获取默认提示词
            let promptId = selectedAIPromptId;
            
            if (!promptId) {
                try {
                    const promptsResponse = await fetch(`${API_BASE}/api/aiprompts/default`);
                    if (promptsResponse.ok) {
                        const defaultPrompt = await promptsResponse.json();
                        if (defaultPrompt && defaultPrompt.id) {
                            promptId = defaultPrompt.id;
                        }
                    }
                } catch (e) {
                    console.warn('获取默认提示词失败:', e);
                }
            }
            
            const requestBody = {
                stockCode: stockCode,
                context: context,
                promptId: promptId,
                modelId: selectedAIModelId || null
            };
            
            const response = await fetch(`${API_BASE}/api/ai/analyze/${stockCode}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(requestBody)
            });
            
            if (!response.ok) {
                const errorText = await response.text();
                throw new Error(errorText);
            }
            
            return await response.text();
        } catch (error) {
            console.error('AI分析失败:', error);
            throw error;
        }
    },
    
    // 新增：显示回测详情
    showBacktestDetail(index) {
        if (!this.batchBacktestResults || !this.batchBacktestResults[index]) return;
        
        const result = this.batchBacktestResults[index];
        
        // 更新详情面板
        document.getElementById('totalReturn').textContent = (result.totalReturn * 100).toFixed(2) + '%';
        document.getElementById('annualReturn').textContent = (result.annualizedReturn * 100).toFixed(2) + '%';
        document.getElementById('maxDrawdown').textContent = (result.maxDrawdown * 100).toFixed(2) + '%';
        document.getElementById('sharpeRatio').textContent = result.sharpeRatio.toFixed(2);
        document.getElementById('tradeCount').textContent = result.totalTrades;
        document.getElementById('winRate').textContent = (result.winRate * 100).toFixed(1) + '%';
        
        // 显示交易记录
        const tradeHistory = document.getElementById('tradeHistory');
        if (result.trades && result.trades.length > 0) {
            tradeHistory.innerHTML = `
                <table>
                    <thead>
                        <tr>
                            <th>日期</th>
                            <th>类型</th>
                            <th>价格</th>
                            <th>数量</th>
                            <th>金额</th>
                            <th>收益</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${result.trades.map(trade => `
                            <tr>
                                <td>${new Date(trade.executedAt).toLocaleDateString()}</td>
                                <td style="color: ${trade.type === 'BUY' ? '#f44336' : '#4caf50'}">${trade.type === 'BUY' ? '买入' : '卖出'}</td>
                                <td>¥${trade.price.toFixed(2)}</td>
                                <td>${trade.quantity}</td>
                                <td>¥${(trade.price * trade.quantity).toFixed(2)}</td>
                                <td style="color: ${trade.profit >= 0 ? '#f44336' : '#4caf50'}">${trade.profit ? trade.profit.toFixed(2) + '%' : '-'}</td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>
            `;
        } else {
            tradeHistory.innerHTML = '<p class="no-data">暂无交易记录</p>';
        }
        
        document.getElementById('backtestDetailPanel').classList.remove('hidden');
        
        // 高亮当前选中的结果项
        document.querySelectorAll('.batch-backtest-item').forEach((item, i) => {
            item.classList.toggle('selected', i === index);
        });
    },
    
    // 新增：一键回测
    async quickBacktest() {
        const stockCode = document.getElementById('quickBacktestStock').value.trim();
        const startDate = document.getElementById('quickBacktestStartDate').value;
        const endDate = document.getElementById('quickBacktestEndDate').value;
        
        if (!stockCode || !startDate || !endDate) {
            alert('请填写完整的回测参数');
            return;
        }
        
        try {
            const resultDiv = document.getElementById('quickBacktestResult');
            const messageDiv = document.getElementById('quickBacktestMessage');
            const detailsDiv = document.getElementById('quickBacktestDetails');
            
            resultDiv.classList.remove('hidden');
            messageDiv.innerHTML = '<div class="loading">正在执行一键回测，请稍候...</div>';
            detailsDiv.innerHTML = '';
            
            // 使用简单移动平均策略进行回测
            const response = await fetch(`${API_BASE}/api/Backtest/run`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({
                    stockCode: stockCode,
                    strategyName: 'MA_CROSS', // 使用默认的移动平均策略
                    startDate: startDate,
                    endDate: endDate,
                    initialCapital: 100000
                })
            });
            
            const result = await response.json();
            
            if (response.ok) {
                messageDiv.innerHTML = `
                    <div style="color: ${result.totalReturn >= 0 ? '#28a745' : '#dc3545'};">
                        <h4>回测完成！</h4>
                        <p>总收益率: <strong>${(result.totalReturn * 100).toFixed(2)}%</strong></p>
                    </div>
                `;
                
                detailsDiv.innerHTML = `
                    <div class="quick-stats">
                        <div>年化收益率: ${(result.annualizedReturn * 100).toFixed(2)}%</div>
                        <div>最大回撤: ${(result.maxDrawdown * 100).toFixed(2)}%</div>
                        <div>夏普比率: ${result.sharpeRatio.toFixed(2)}</div>
                        <div>交易次数: ${result.totalTrades}</div>
                        <div>胜率: ${(result.winRate * 100).toFixed(1)}%</div>
                    </div>
                `;
            } else {
                messageDiv.innerHTML = `<div style="color: #dc3545;">回测失败: ${result.message || '未知错误'}</div>`;
            }
        } catch (error) {
            console.error('一键回测失败:', error);
            document.getElementById('quickBacktestMessage').innerHTML = `<div style="color: #dc3545;">回测失败: ${error.message}</div>`;
        }
    }
};

// 策略优化相关功能
const strategyOptimization = {
    currentOptimizationId: null,
    
    // 加载策略参数配置
    async loadStrategyParameters() {
        const strategyName = document.getElementById('optimizationStrategySelect').value;
        if (!strategyName) {
            alert('请先选择要优化的策略');
            return;
        }
        
        try {
            // 根据策略名称获取参数配置
            const parametersDiv = document.getElementById('parameterRanges');
            const optimizationParams = document.getElementById('optimizationParameters');
            
            // 显示参数配置区域
            optimizationParams.classList.remove('hidden');
            
            // 根据不同策略类型显示不同的参数配置
            let parameterHTML = '';
            
            if (strategyName.toLowerCase().includes('ma-cross')) {
                parameterHTML = `
                    <div class="parameter-config">
                        <h5>短期移动平均周期</h5>
                        <div class="parameter-range">
                            <label>最小值:</label>
                            <input type="number" id="shortMA_min" value="5" min="1" max="50">
                            <label>最大值:</label>
                            <input type="number" id="shortMA_max" value="20" min="1" max="50">
                            <label>步长:</label>
                            <input type="number" id="shortMA_step" value="5" min="1" max="10">
                        </div>
                    </div>
                    <div class="parameter-config">
                        <h5>长期移动平均周期</h5>
                        <div class="parameter-range">
                            <label>最小值:</label>
                            <input type="number" id="longMA_min" value="20" min="10" max="100">
                            <label>最大值:</label>
                            <input type="number" id="longMA_max" value="50" min="10" max="100">
                            <label>步长:</label>
                            <input type="number" id="longMA_step" value="10" min="1" max="20">
                        </div>
                    </div>
                `;
            } else if (strategyName.toLowerCase().includes('rsi')) {
                parameterHTML = `
                    <div class="parameter-config">
                        <h5>RSI周期</h5>
                        <div class="parameter-range">
                            <label>最小值:</label>
                            <input type="number" id="rsiPeriod_min" value="10" min="5" max="30">
                            <label>最大值:</label>
                            <input type="number" id="rsiPeriod_max" value="20" min="5" max="30">
                            <label>步长:</label>
                            <input type="number" id="rsiPeriod_step" value="2" min="1" max="5">
                        </div>
                    </div>
                    <div class="parameter-config">
                        <h5>超买阈值</h5>
                        <div class="parameter-range">
                            <label>最小值:</label>
                            <input type="number" id="overbought_min" value="70" min="60" max="85">
                            <label>最大值:</label>
                            <input type="number" id="overbought_max" value="80" min="60" max="85">
                            <label>步长:</label>
                            <input type="number" id="overbought_step" value="5" min="1" max="10">
                        </div>
                    </div>
                    <div class="parameter-config">
                        <h5>超卖阈值</h5>
                        <div class="parameter-range">
                            <label>最小值:</label>
                            <input type="number" id="oversold_min" value="20" min="15" max="40">
                            <label>最大值:</label>
                            <input type="number" id="oversold_max" value="30" min="15" max="40">
                            <label>步长:</label>
                            <input type="number" id="oversold_step" value="5" min="1" max="10">
                        </div>
                    </div>
                `;
            } else {
                parameterHTML = `
                    <div class="parameter-config">
                        <p>该策略暂不支持参数优化，或请手动配置参数范围。</p>
                        <div class="parameter-range">
                            <label>自定义参数1:</label>
                            <input type="text" id="custom1_name" placeholder="参数名称">
                            <input type="number" id="custom1_min" placeholder="最小值">
                            <input type="number" id="custom1_max" placeholder="最大值">
                            <input type="number" id="custom1_step" placeholder="步长">
                        </div>
                    </div>
                `;
            }
            
            parametersDiv.innerHTML = parameterHTML;
            
        } catch (error) {
            console.error('加载策略参数失败:', error);
            alert('加载策略参数失败: ' + error.message);
        }
    },
    
    // 开始优化
    async startOptimization() {
        const strategyName = document.getElementById('optimizationStrategySelect').value;
        const stockCode = document.getElementById('optimizationStockCode').value.trim();
        const startDate = document.getElementById('optimizationStartDate').value;
        const endDate = document.getElementById('optimizationEndDate').value;
        const target = document.getElementById('optimizationTarget').value;
        
        if (!strategyName || !stockCode || !startDate || !endDate) {
            alert('请填写完整的优化配置');
            return;
        }
        if (new Date(startDate) >= new Date(endDate)) {
            alert('开始日期必须早于结束日期');
            return;
        }
        
        try {
            // 将策略名称映射到ID
            const match = (quantTrading._strategies || []).find(s => s.name === strategyName);
            if (!match) {
                alert('未找到该策略，请刷新策略列表后重试');
                return;
            }
            const strategyId = match.id;

            // 收集参数范围（用于覆盖默认优化配置）
            const parameters = this.collectParameterRanges();

            // 显示加载状态
            document.getElementById('optimizationProgress').classList.remove('hidden');
            document.getElementById('optimizationResults').classList.add('hidden');
            const progressBar = document.getElementById('optimizationProgressBar');
            const progressText = document.getElementById('optimizationProgressText');
            progressBar.style.width = '0%';
            progressText.textContent = '正在计算优化结果...';

            // 获取默认优化配置并覆盖为UI设置
            let optimizationConfig = null;
            try {
                const confResp = await fetch(`${API_BASE}/api/StrategyOptimization/default-config`);
                if (confResp.ok) optimizationConfig = await confResp.json();
            } catch {}

            const targetMap = { totalReturn: 'TotalReturn', sharpeRatio: 'SharpeRatio', maxDrawdown: 'MaxDrawdown', winRate: 'WinRate' };
            if (!optimizationConfig) optimizationConfig = {};
            optimizationConfig.Target = targetMap[target] || 'TotalReturn';

            const getParam = (name) => parameters.find(p => p.name === name);
            const shortP = getParam('短周期');
            const longP = getParam('长周期');
            const overBought = getParam('超买阈值');
            const overSold = getParam('超卖阈值');
            if (shortP) optimizationConfig.ShortPeriodRange = { Min: shortP.minValue, Max: shortP.maxValue, Step: shortP.step };
            if (longP) optimizationConfig.LongPeriodRange = { Min: longP.minValue, Max: longP.maxValue, Step: longP.step };
            if (overBought) optimizationConfig.RSIOverBoughtRange = { Min: overBought.minValue, Max: overBought.maxValue, Step: overBought.step };
            if (overSold) optimizationConfig.RSIOverSoldRange = { Min: overSold.minValue, Max: overSold.maxValue, Step: overSold.step };
            const hasAnyRange = !!(shortP || longP || overBought || overSold);
            if (!hasAnyRange) optimizationConfig = null; // 使用后端默认

            // 发送优化请求（匹配后端模型）
            const response = await fetch(`${API_BASE}/api/StrategyOptimization/optimize`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    strategyId: strategyId,
                    stockCodes: [stockCode],
                    startDate: startDate,
                    endDate: endDate,
                    optimizationConfig: optimizationConfig
                })
            });

            if (!response.ok) {
                let msg = '';
                try { msg = await response.text(); } catch {}
                throw new Error(`优化请求失败: ${response.status} ${msg}`);
            }

            const result = await response.json();
            // 保存关键结果
            this.lastOptimizationResult = result;
            this.currentOptimizationResultId = result.id;
            this.currentStrategyId = result.strategyId;

            // 展示结果
            document.getElementById('optimizationProgress').classList.add('hidden');
            document.getElementById('optimizationResults').classList.remove('hidden');
            const bestParamsDiv = document.getElementById('bestParameters');
            try {
                const parsedParams = typeof result.optimizedParameters === 'string' ? JSON.parse(result.optimizedParameters) : (result.optimizedParameters || {});
                bestParamsDiv.innerHTML = Object.entries(parsedParams).map(([k, v]) => `
                    <div class="parameter-item"><span class="param-name">${k}:</span><span class="param-value">${v}</span></div>
                `).join('') || '<div class="parameter-item">无参数</div>';
            } catch {
                bestParamsDiv.innerHTML = '<div class="parameter-item">参数解析失败</div>';
            }
            document.getElementById('bestTotalReturn').textContent = ((Number(result.totalReturn) || 0) * 100).toFixed(2) + '%';
            document.getElementById('bestSharpeRatio').textContent = (Number(result.sharpeRatio) || 0).toFixed(3);
            document.getElementById('bestMaxDrawdown').textContent = ((Number(result.maxDrawdown) || 0) * 100).toFixed(2) + '%';
            document.getElementById('bestWinRate').textContent = ((Number(result.winRate) || 0) * 100).toFixed(2) + '%';

            // 加载历史
            this.loadOptimizationHistory(this.currentStrategyId);

        } catch (error) {
            console.error('启动优化失败:', error);
            alert('启动优化失败: ' + error.message);
            document.getElementById('optimizationProgress').classList.add('hidden');
        }
    },
    
    // 收集参数范围配置
    collectParameterRanges() {
        const parameters = [];
        const parameterConfigs = document.querySelectorAll('.parameter-config');
        
        parameterConfigs.forEach(config => {
            const title = config.querySelector('h5');
            if (!title) return;
            
            const paramName = title.textContent.trim();
            const inputs = config.querySelectorAll('input[type="number"]');
            
            if (inputs.length >= 3) {
                const min = parseFloat(inputs[0].value);
                const max = parseFloat(inputs[1].value);
                const step = parseFloat(inputs[2].value);
                
                if (!isNaN(min) && !isNaN(max) && !isNaN(step) && min < max && step > 0) {
                    parameters.push({
                        name: paramName,
                        minValue: min,
                        maxValue: max,
                        step: step
                    });
                }
            }
        });
        
        return parameters;
    },
    
    // 轮询优化进度
    async pollOptimizationProgress() { return; },
    
    // 加载优化结果
    async loadOptimizationResult() { return; },
    
    // 停止优化
    async stopOptimization() {
        if (!this.currentOptimizationId) return;
        
        try {
            const response = await fetch(`${API_BASE}/api/strategyoptimization/stop/${this.currentOptimizationId}`, {
                method: 'POST'
            });
            
            if (response.ok) {
                document.getElementById('optimizationProgress').classList.add('hidden');
                this.currentOptimizationId = null;
                alert('优化已停止');
            }
        } catch (error) {
            console.error('停止优化失败:', error);
        }
    },
    
    // 应用最佳参数
    async applyBestParameters() {
        if (!this.currentOptimizationResultId || !this.currentStrategyId) {
            alert('没有可应用的优化结果');
            return;
        }
        try {
            const response = await fetch(`${API_BASE}/api/StrategyOptimization/${this.currentStrategyId}/apply/${this.currentOptimizationResultId}`, {
                method: 'POST'
            });
            if (response.ok) {
                alert('最佳参数已应用到策略配置');
                quantTrading.loadStrategies();
            } else {
                const txt = await response.text();
                throw new Error(`应用参数失败: ${response.status} ${txt}`);
            }
        } catch (error) {
            console.error('应用最佳参数失败:', error);
            alert('应用最佳参数失败: ' + error.message);
        }
    },
    
    // 查看详细结果
    async viewDetailedResults() {
        if (!this.lastOptimizationResult) {
            alert('没有可查看的优化结果');
            return;
        }
        const r = this.lastOptimizationResult;
        const detailWindow = window.open('', '_blank', 'width=800,height=600');
        let paramsHtml = '';
        try {
            const parsedParams = typeof r.optimizedParameters === 'string' ? JSON.parse(r.optimizedParameters) : (r.optimizedParameters || {});
            paramsHtml = Object.entries(parsedParams).map(([k, v]) => `${k}: ${v}`).join(', ');
        } catch { paramsHtml = String(r.optimizedParameters || '') }
        detailWindow.document.write(`
            <html>
            <head>
                <title>优化详细结果</title>
                <style>
                    body { font-family: Arial, sans-serif; margin: 20px; }
                    table { border-collapse: collapse; width: 100%; }
                    th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }
                    th { background-color: #f2f2f2; }
                </style>
            </head>
            <body>
                <h2>优化详细结果</h2>
                <p><strong>策略ID:</strong> ${r.strategyId} | <strong>股票:</strong> ${r.stockCodes}</p>
                <p><strong>周期:</strong> ${new Date(r.startDate).toLocaleDateString()} - ${new Date(r.endDate).toLocaleDateString()}</p>
                <p><strong>测试组合:</strong> ${r.testedCombinations}/${r.totalCombinations} | <strong>耗时:</strong> ${r.optimizationDuration}</p>
                <p><strong>最佳参数:</strong> ${paramsHtml}</p>
                <table>
                    <thead>
                        <tr>
                            <th>总收益率</th>
                            <th>夏普比率</th>
                            <th>最大回撤</th>
                            <th>胜率</th>
                            <th>总交易数</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr>
                            <td>${((Number(r.totalReturn) || 0) * 100).toFixed(2)}%</td>
                            <td>${(Number(r.sharpeRatio) || 0).toFixed(3)}</td>
                            <td>${((Number(r.maxDrawdown) || 0) * 100).toFixed(2)}%</td>
                            <td>${((Number(r.winRate) || 0) * 100).toFixed(2)}%</td>
                            <td>${r.totalTrades ?? '-'}</td>
                        </tr>
                    </tbody>
                </table>
            </body>
            </html>
        `);
    },
    
    // 加载优化历史
    async loadOptimizationHistory(strategyId) {
        try {
            if (!strategyId) {
                const strategyName = document.getElementById('optimizationStrategySelect')?.value;
                const match = (quantTrading._strategies || []).find(s => s.name === strategyName);
                if (match) strategyId = match.id;
            }
            if (!strategyId) {
                document.getElementById('optimizationHistory').innerHTML = '<p class="no-data">请选择策略以查看历史</p>';
                return;
            }
            const response = await fetch(`${API_BASE}/api/StrategyOptimization/${strategyId}/history`);
            if (!response.ok) {
                throw new Error(`获取历史失败: ${response.status}`);
            }
            const history = await response.json();
            const historyDiv = document.getElementById('optimizationHistory');
            if (!Array.isArray(history) || history.length === 0) {
                historyDiv.innerHTML = '<p class="no-data">暂无优化历史记录</p>';
                return;
            }
            historyDiv.innerHTML = history.map(item => `
                <div class="optimization-history-item">
                    <div class="history-header">
                        <div class="history-title">
                            <strong>策略 #${item.strategyId}</strong>
                        </div>
                        <div class="history-date">
                            ${new Date(item.createdAt).toLocaleString()}
                        </div>
                    </div>
                    <div class="history-details">
                        <div class="history-metrics">
                            <span>收益率: ${((Number(item.totalReturn) || 0) * 100).toFixed(2)}%</span>
                            <span>夏普比率: ${(Number(item.sharpeRatio) || 0).toFixed(3)}</span>
                            <span>最大回撤: ${((Number(item.maxDrawdown) || 0) * 100).toFixed(2)}%</span>
                            <span>胜率: ${((Number(item.winRate) || 0) * 100).toFixed(2)}%</span>
                        </div>
                        <div class="history-actions">
                            <button class="btn btn-small" onclick="strategyOptimization.viewHistoryDetails(${item.id})">查看详情</button>
                            <button class="btn btn-small" onclick="strategyOptimization.applyHistoryParameters(${item.id})">应用参数</button>
                        </div>
                    </div>
                </div>
            `).join('');
        } catch (error) {
            console.error('加载优化历史失败:', error);
            document.getElementById('optimizationHistory').innerHTML = '<p class="error">加载历史失败: ' + error.message + '</p>';
        }
    },
    
    // 查看历史详情
    async viewHistoryDetails(optimizationId) {
        // 尝试通过当前选择的策略加载历史并定位该条目
        let strategyId = this.currentStrategyId;
        if (!strategyId) {
            const strategyName = document.getElementById('optimizationStrategySelect')?.value;
            const match = (quantTrading._strategies || []).find(s => s.name === strategyName);
            strategyId = match?.id;
        }
        if (!strategyId) {
            alert('请先选择策略');
            return;
        }
        try {
            const resp = await fetch(`${API_BASE}/api/StrategyOptimization/${strategyId}/history`);
            if (!resp.ok) throw new Error(`加载历史失败: ${resp.status}`);
            const list = await resp.json();
            const item = list.find(x => x.id === optimizationId);
            if (!item) {
                alert('未在历史中找到该优化结果');
                return;
            }
            this.lastOptimizationResult = item;
            this.currentOptimizationResultId = item.id;
            this.currentStrategyId = item.strategyId;
            await this.viewDetailedResults();
        } catch (e) {
            console.error('查看历史详情失败:', e);
            alert('查看历史详情失败: ' + e.message);
        }
    },
    
    // 应用历史参数
    async applyHistoryParameters(optimizationId) {
        // 依赖当前策略ID；如果未知则尝试推断
        if (!this.currentStrategyId) {
            const strategyName = document.getElementById('optimizationStrategySelect')?.value;
            const match = (quantTrading._strategies || []).find(s => s.name === strategyName);
            this.currentStrategyId = match?.id || this.currentStrategyId;
        }
        this.currentOptimizationResultId = optimizationId;
        await this.applyBestParameters();
    },
    
    // 清空历史
    async clearHistory() {
        if (!confirm('确定要清空所有优化历史记录吗？此操作不可撤销。')) {
            return;
        }
        
        try {
            const response = await fetch(`${API_BASE}/api/strategyoptimization/history`, {
                method: 'DELETE'
            });
            
            if (response.ok) {
                alert('历史记录已清空');
                this.loadOptimizationHistory();
            } else {
                throw new Error(`清空历史失败: ${response.status}`);
            }
        } catch (error) {
            console.error('清空历史失败:', error);
            alert('清空历史失败: ' + error.message);
        }
    }
};

// AI设置相关函数
async function showAISettingsModal() {
    try {
        // 加载AI模型配置列表
        const modelsResponse = await fetch(`${API_BASE}/api/aimodelconfig`);
        if (modelsResponse.ok) {
            const models = await modelsResponse.json();
            const modelSelect = document.getElementById('aiModelSelect');
            modelSelect.innerHTML = '<option value="">使用默认配置</option>';
            
            models.forEach(model => {
                const option = document.createElement('option');
                option.value = model.id;
                option.textContent = `${model.name} (${model.modelName})`;
                if (model.id === selectedAIModelId) {
                    option.selected = true;
                }
                modelSelect.appendChild(option);
            });
        }
        
        // 加载AI提示词列表
        const promptsResponse = await fetch(`${API_BASE}/api/aiprompts`);
        if (promptsResponse.ok) {
            const prompts = await promptsResponse.json();
            const promptSelect = document.querySelector('#aiSettingsModal #aiPromptSelect');
            promptSelect.innerHTML = '<option value="">使用默认提示词</option>';
            
            prompts.forEach(prompt => {
                if (prompt.isActive) {
                    const option = document.createElement('option');
                    option.value = prompt.id;
                    option.textContent = prompt.name;
                    if (prompt.id === selectedAIPromptId) {
                        option.selected = true;
                    }
                    promptSelect.appendChild(option);
                }
            });
        }
        
        // 显示模态框
        document.getElementById('aiSettingsModal').style.display = 'block';
    } catch (error) {
        console.error('加载AI设置失败:', error);
        alert('加载AI设置失败: ' + error.message);
    }
}

// 从localStorage恢复AI设置选择
function loadAISettings() {
    const savedModelId = localStorage.getItem('selectedAIModelId');
    const savedPromptId = localStorage.getItem('selectedAIPromptId');
    
    if (savedModelId) {
        selectedAIModelId = parseInt(savedModelId);
    }
    
    if (savedPromptId) {
        selectedAIPromptId = parseInt(savedPromptId);
    }
}

function applyAISettings() {
    const modelSelect = document.getElementById('aiModelSelect');
    const promptSelect = document.querySelector('#aiSettingsModal #aiPromptSelect');
    
    // 更新选中的AI模型和提示词
    selectedAIModelId = modelSelect.value ? parseInt(modelSelect.value) : null;
    selectedAIPromptId = promptSelect.value ? parseInt(promptSelect.value) : null;
    
    // 保存选择到localStorage
    if (selectedAIModelId) {
        localStorage.setItem('selectedAIModelId', selectedAIModelId.toString());
    } else {
        localStorage.removeItem('selectedAIModelId');
    }
    
    if (selectedAIPromptId) {
        localStorage.setItem('selectedAIPromptId', selectedAIPromptId.toString());
    } else {
        localStorage.removeItem('selectedAIPromptId');
    }
    
    // 更新显示的AI设置信息
    const modelName = modelSelect.selectedOptions[0]?.textContent || '默认';
    const promptName = promptSelect.selectedOptions[0]?.textContent || '默认';
    
    document.getElementById('aiModelName').textContent = modelName;
    document.getElementById('aiPromptName').textContent = promptName;
    
    // 关闭模态框
    document.getElementById('aiSettingsModal').style.display = 'none';
    
    // 提示用户设置已更新
    console.log('AI设置已更新:', { modelId: selectedAIModelId, promptId: selectedAIPromptId });
}

// 页面加载完成后初始化
document.addEventListener('DOMContentLoaded', function() {
    // 初始化分类下拉框
    loadCategories();
    
    // 加载自选股数据
    loadWatchlist();
    
    // 加载设置
    loadSettings();
    
    // 恢复AI设置选择
    loadAISettings();
    
    // 初始化AI配置管理器
    aiConfigManager.init();
    
    // 初始化AI提示词管理器
    aiPromptManager.init();
    
    // 初始化选股模板管理器
    screenTemplateManager.init();
    
    // 初始化量化交易
    quantTrading.loadStrategies();
    
    // 启动自动刷新
    startAutoRefresh();
    
    // 设置默认日期
    const today = new Date();
    const oneMonthAgo = new Date(today.getTime() - 30 * 24 * 60 * 60 * 1000);
    
    // 设置回测日期
    document.getElementById('backtestStartDate').value = oneMonthAgo.toISOString().split('T')[0];
    document.getElementById('backtestEndDate').value = today.toISOString().split('T')[0];
    
    // 设置一键回测日期
    document.getElementById('quickBacktestStartDate').value = oneMonthAgo.toISOString().split('T')[0];
    document.getElementById('quickBacktestEndDate').value = today.toISOString().split('T')[0];
    
    // 设置策略优化日期
    const optimizationStartDate = document.getElementById('optimizationStartDate');
    const optimizationEndDate = document.getElementById('optimizationEndDate');
    if (optimizationStartDate && optimizationEndDate) {
        optimizationStartDate.value = oneMonthAgo.toISOString().split('T')[0];
        optimizationEndDate.value = today.toISOString().split('T')[0];
    }
    
    // 绑定事件
    const backtestStockSelect = document.getElementById('backtestStockSelect');
    if (backtestStockSelect) {
        backtestStockSelect.addEventListener('change', function() {
            quantTrading.onBacktestStockChange();
        });
    }
    
    // 绑定策略优化相关事件
    const optimizationStrategySelect = document.getElementById('optimizationStrategySelect');
    if (optimizationStrategySelect) {
        optimizationStrategySelect.addEventListener('change', function() {
            strategyOptimization.loadStrategyParameters();
        });
    }
    
    // 确保switchTab函数在全局作用域中可用
    window.switchTab = switchTab;
});