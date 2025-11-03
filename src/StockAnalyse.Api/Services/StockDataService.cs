using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockAnalyse.Api.Data;
using StockAnalyse.Api.Models;
using StockAnalyse.Api.Services.Interfaces;

namespace StockAnalyse.Api.Services;

public class StockDataService : IStockDataService
{
    private readonly StockDbContext _context;
    private readonly ILogger<StockDataService> _logger;
    private readonly HttpClient _httpClient;

    public StockDataService(StockDbContext context, ILogger<StockDataService> logger, HttpClient httpClient)
    {
        _context = context;
        _logger = logger;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// 安全地将字符串转换为decimal，处理 "-"、空字符串等情况
    /// </summary>
    private decimal SafeConvertToDecimal(object? value, decimal defaultValue = 0)
    {
        if (value == null)
            return defaultValue;
        
        string? strValue = value.ToString();
        if (string.IsNullOrWhiteSpace(strValue) || strValue == "-" || strValue == "--")
            return defaultValue;
        
        if (decimal.TryParse(strValue, out decimal result))
            return result;
        
        return defaultValue;
    }

    public async Task<Stock?> GetRealTimeQuoteAsync(string stockCode)
    {
        try
        {
            // 优先尝试从东方财富获取
            var stock = await FetchEastMoneyDataAsync(stockCode);
            
            // 如果东方财富失败，尝试新浪财经
            if (stock == null)
            {
                stock = await FetchSinaDataAsync(stockCode);
            }
            
            if (stock != null)
            {
                await SaveStockDataAsync(stock);
            }
            
            return stock;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取股票行情失败: {StockCode}", stockCode);
            
            // 如果API失败，从数据库获取最新数据
            return await _context.Stocks.FirstOrDefaultAsync(s => s.Code == stockCode);
        }
    }

    public async Task<Stock?> GetWatchlistRealTimeQuoteAsync(string stockCode)
    {
        try
        {
            // 优先尝试从东方财富获取
            var stock = await FetchEastMoneyDataAsync(stockCode);
            
            // 如果东方财富失败，尝试新浪财经
            if (stock == null)
            {
                stock = await FetchSinaDataAsync(stockCode);
            }
            
            // 注意：这里不保存到数据库，确保总是获取最新数据
            return stock;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取自选股实时行情失败: {StockCode}", stockCode);
            return null;
        }
    }

    public async Task<List<Stock>> GetBatchQuotesAsync(IEnumerable<string> stockCodes)
    {
        var codes = stockCodes.ToList();
        var stocks = new List<Stock>();
        
        foreach (var code in codes)
        {
            var stock = await GetRealTimeQuoteAsync(code);
            if (stock != null)
            {
                stocks.Add(stock);
            }
        }
        
        return stocks;
    }

    public async Task SaveStockDataAsync(Stock stock)
    {
        var existing = await _context.Stocks.FirstOrDefaultAsync(s => s.Code == stock.Code);
        
        if (existing != null)
        {
            // 更新现有记录，包括股票名称和其他重要信息
            existing.Name = stock.Name;  // 添加了名称更新
            existing.Market = stock.Market;  // 添加了市场信息更新
            existing.CurrentPrice = stock.CurrentPrice;
            existing.OpenPrice = stock.OpenPrice;
            existing.ClosePrice = stock.ClosePrice;
            existing.HighPrice = stock.HighPrice;
            existing.LowPrice = stock.LowPrice;
            existing.Volume = stock.Volume;
            existing.Turnover = stock.Turnover;
            existing.ChangePercent = stock.ChangePercent;
            existing.ChangeAmount = stock.ChangeAmount;
            existing.TurnoverRate = stock.TurnoverRate;
            existing.PE = stock.PE;  // 添加了市盈率更新
            existing.PB = stock.PB;  // 添加了市净率更新
            existing.LastUpdate = DateTime.Now;
        }
        else
        {
            stock.LastUpdate = DateTime.Now;
            await _context.Stocks.AddAsync(stock);
        }
        
        await _context.SaveChangesAsync();
    }

    public async Task<List<StockHistory>> GetDailyDataAsync(string stockCode, DateTime startDate, DateTime endDate)
    {
        return await _context.StockHistories
            .Where(h => h.StockCode == stockCode && h.TradeDate >= startDate && h.TradeDate <= endDate)
            .OrderBy(h => h.TradeDate)
            .ToListAsync();
    }

    public async Task<(decimal macd, decimal signal, decimal histogram)> CalculateMACDAsync(string stockCode)
    {
        // 获取最近的数据
        var histories = await _context.StockHistories
            .Where(h => h.StockCode == stockCode)
            .OrderByDescending(h => h.TradeDate)
            .Take(100)
            .ToListAsync();
            
        if (histories.Count < 26)
        {
            return (0, 0, 0);
        }
        
        // 简化的MACD计算（实际应该使用标准的EMA算法）
        var closes = histories.Select(h => h.Close).Reverse().ToArray();
        
        // EMA12
        var ema12 = CalculateEMA(closes, 12);
        // EMA26
        var ema26 = CalculateEMA(closes, 26);
        
        var macd = ema12 - ema26;
        
        // 简化的Signal计算
        var signal = macd * 0.9m;
        var histogram = macd - signal;
        
        // 不再更新数据库中的MACD字段，因为这些字段已被删除
        // 只返回计算结果供调用方使用
        
        return (macd, signal, histogram);
    }

    public async Task<List<Stock>> GetRankingListAsync(string market, int top = 100)
    {
        return await _context.Stocks
            .Where(s => s.Market == market)
            .OrderByDescending(s => s.ChangePercent)
            .Take(top)
            .ToListAsync();
    }

    private Stock? ParseSinaData(string data, string stockCode)
    {
        try
        {
            // 新浪财经返回格式示例：var hq_str_sz000001="平安银行,19.82,19.82,19.70,..."
            // 需要正确解析返回的数据格式
            var match = System.Text.RegularExpressions.Regex.Match(data, @"\""(.*?)\""");
            if (!match.Success)
            {
                _logger.LogWarning("无法解析新浪财经返回数据: {Data}", data);
                return null;
            }
            
            var parts = match.Groups[1].Value.Split(',');
            if (parts.Length < 32)
            {
                _logger.LogWarning("新浪财经返回数据字段不足: {Data}", data);
                return null;
            }
            
            var name = parts[0]; // 股票名称在第一个位置
            var open = SafeConvertToDecimal(parts[1]);
            var prevClose = SafeConvertToDecimal(parts[2]);
            var current = SafeConvertToDecimal(parts[3]) / 100;
            var high = SafeConvertToDecimal(parts[4]);
            var low = SafeConvertToDecimal(parts[5]);
            var volume = SafeConvertToDecimal(parts[8]);
            var turnover = SafeConvertToDecimal(parts[9]);
            
            var changeAmount = current - prevClose;
            var changePercent = prevClose != 0 ? changeAmount / prevClose * 100 : 0;
            var turnoverRate = volume > 0 ? turnover / volume * 100 : 0;
            
            return new Stock
            {
                Code = stockCode,
                Name = name,
                Market = stockCode.StartsWith("6") ? "SH" : "SZ",
                CurrentPrice = current,
                OpenPrice = open,
                ClosePrice = prevClose,
                HighPrice = high,
                LowPrice = low,
                Volume = volume,
                Turnover = turnover,
                ChangeAmount = changeAmount,
                ChangePercent = changePercent,
                TurnoverRate = turnoverRate
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析股票数据失败: {Data}", data);
            return null;
        }
    }

    private decimal CalculateEMA(decimal[] prices, int period)
    {
        if (prices.Length == 0)
            return 0;
            
        decimal multiplier = 2m / (period + 1);
        decimal ema = prices[0];
        
        for (int i = 1; i < prices.Length; i++)
        {
            ema = (prices[i] * multiplier) + (ema * (1 - multiplier));
        }
        
        return ema;
    }
    
    /// <summary>
    /// 从东方财富获取股票数据
    /// </summary>
    private async Task<Stock?> FetchEastMoneyDataAsync(string stockCode)
    {
        try
        {
            // 判断市场：1=上交所, 0=深交所
            var market = stockCode.StartsWith("6") ? "1" : "0";
            var secid = $"{market}.{stockCode}";
            
            // 补充PE(PETTM f162)与PB(f167)字段，避免PE/PB一直为0导致筛选结果为空
            var url = $"https://push2.eastmoney.com/api/qt/stock/get?secid={secid}&fields=f57,f58,f107,f137,f43,f46,f44,f45,f47,f48,f168,f60,f170,f116,f171,f117,f172,f169,f162,f167&fltt=2";
            
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.DefaultRequestHeaders.Add("Referer", "http://quote.eastmoney.com/");
            
            var response = await _httpClient.GetStringAsync(url);
            
            dynamic? data = Newtonsoft.Json.JsonConvert.DeserializeObject(response);
            
            if (data?.data == null)
            {
                return null;
            }
            
            var stockInfo = data.data;
            
            // 获取各个价格字段（使用安全转换方法，处理"-"等无效值）
            decimal currentPrice = SafeConvertToDecimal(stockInfo.f43);
            decimal openPrice = SafeConvertToDecimal(stockInfo.f46);
            decimal closePrice = SafeConvertToDecimal(stockInfo.f60); // 昨收价
            decimal highPrice = SafeConvertToDecimal(stockInfo.f44);
            decimal lowPrice = SafeConvertToDecimal(stockInfo.f45);
            
            // 价格回退逻辑：非交易时间使用昨收价
            if (currentPrice == 0.0m && closePrice > 0.0m)
                currentPrice = closePrice;
            
            if (openPrice == 0 && closePrice > 0)
                openPrice = closePrice;
            
            if (highPrice == 0) 
                highPrice = currentPrice;
            if (lowPrice == 0) 
                lowPrice = currentPrice;
            
            var stock = new Stock
            {
                Code = stockCode,
                Name = stockInfo.f58?.ToString() ?? "未知",
                Market = stockCode.StartsWith("6") ? "SH" : "SZ",
                CurrentPrice = currentPrice,
                OpenPrice = openPrice,
                ClosePrice = closePrice,
                HighPrice = highPrice,
                LowPrice = lowPrice,
                Volume = SafeConvertToDecimal(stockInfo.f47),
                Turnover = SafeConvertToDecimal(stockInfo.f48),
                ChangeAmount = SafeConvertToDecimal(stockInfo.f169),
                ChangePercent = SafeConvertToDecimal(stockInfo.f170),
                TurnoverRate = SafeConvertToDecimal(stockInfo.f168),
                PE = SafeConvertToDecimal(stockInfo.f162) > 0 ? SafeConvertToDecimal(stockInfo.f162) : null,  // 市盈率
                PB = SafeConvertToDecimal(stockInfo.f167) > 0 ? SafeConvertToDecimal(stockInfo.f167) : null,  // 市净率
                LastUpdate = DateTime.Now
            };
            
            return stock;
        }
        catch (Exception)
        {
            return null;
        }
    }
    
    /// <summary>
    /// 从新浪财经获取（备用）
    /// </summary>
    private async Task<Stock?> FetchSinaDataAsync(string stockCode)
    {
        try
        {
            // 根据股票代码确定市场前缀
            var marketPrefix = stockCode.StartsWith("6") ? "sh" : "sz";
            var url = $"http://hq.sinajs.cn/list={marketPrefix}{stockCode}";
            _logger.LogInformation("请求新浪财经接口: {Url}", url);
            
            // 设置请求头
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.DefaultRequestHeaders.Add("Referer", "http://finance.sina.com.cn");
            
            var response = await _httpClient.GetStringAsync(url);
            _logger.LogInformation("新浪财经返回数据: {Response}", response);
            
            var stock = ParseSinaData(response, stockCode);
            
            if (stock != null)
            {
                _logger.LogInformation("成功从新浪财经获取: {Code} {Name}", stock.Code, stock.Name);
            }
            else
            {
                _logger.LogWarning("解析新浪财经数据失败: {Response}", response);
            }
            
            return stock;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "新浪财经获取失败: {Code}", stockCode);
            return null;
        }
    }

    public async Task<int> FetchAndStoreDailyHistoryAsync(string stockCode, DateTime startDate, DateTime endDate)
    {
        int saved = 0;
        try
        {
            var market = stockCode.StartsWith("6") ? "1" : "0";
            var secid = $"{market}.{stockCode}";
            var beg = startDate.ToString("yyyyMMdd");
            var end = endDate.ToString("yyyyMMdd");

            var url = $"http://push2his.eastmoney.com/api/qt/stock/kline/get?secid={secid}&fields1=f1,f2,f3,f4&fields2=f51,f52,f53,f54,f55,f56,f57&klt=101&fqt=1&beg={beg}&end={end}";
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            _httpClient.DefaultRequestHeaders.Add("Referer", "http://quote.eastmoney.com/");

            var response = await _httpClient.GetStringAsync(url);
            dynamic? data = Newtonsoft.Json.JsonConvert.DeserializeObject(response);
            if (data?.data?.klines == null)
            {
                _logger.LogWarning("东方财富日线数据为空: {Code}", stockCode);
                return 0;
            }

            foreach (var k in data.data.klines)
            {
                string line = k.ToString(); // "YYYY-MM-DD,open,close,high,low,volume,amount"
                var parts = line.Split(',');
                if (parts.Length < 7) continue;

                try
                {
                    var tradeDate = DateTime.Parse(parts[0]);
                    var open = SafeConvertToDecimal(parts[1]);
                    var close = SafeConvertToDecimal(parts[2]);
                    var high = SafeConvertToDecimal(parts[3]);
                    var low = SafeConvertToDecimal(parts[4]);
                    var volume = SafeConvertToDecimal(parts[5]);
                    var amount = SafeConvertToDecimal(parts[6]);

                    // 跳过无效数据（价格为0或负数）
                    if (open <= 0 || close <= 0 || high <= 0 || low <= 0)
                        continue;

                    var existing = await _context.StockHistories
                        .FirstOrDefaultAsync(h => h.StockCode == stockCode && h.TradeDate == tradeDate);

                    if (existing != null)
                    {
                        existing.Open = open;
                        existing.Close = close;
                        existing.High = high;
                        existing.Low = low;
                        existing.Volume = volume;
                        existing.Turnover = amount;
                    }
                    else
                    {
                        await _context.StockHistories.AddAsync(new StockHistory
                        {
                            StockCode = stockCode,
                            TradeDate = tradeDate,
                            Open = open,
                            Close = close,
                            High = high,
                            Low = low,
                            Volume = volume,
                            Turnover = amount
                        });
                    }
                    
                    saved++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("解析日线数据失败: {Line}, 错误: {Error}", line, ex.Message);
                    continue;
                }
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("保存{Count}条 {Code} 日线历史", saved, stockCode);
            return saved;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "拉取并保存东方财富日线失败: {Code}", stockCode);
            return 0;
        }
    }

    /// <summary>
    /// 从东方财富获取指定市场的所有股票实时行情（用于选股）
    /// </summary>
    public async Task<List<Stock>> FetchAllStocksFromEastMoneyAsync(string? market = null, int maxCount = 5000)
    {
        var allStocks = new List<Stock>();
        
        try
        {
            _logger.LogInformation("开始从东方财富获取股票列表，市场: {Market}, 最大数量: {MaxCount}", 
                market ?? "全部", maxCount);
            
            // 构建筛选条件
            // m:1 表示上交所, m:2 表示深交所, 不指定则获取全部
            string fs = "";
            if (!string.IsNullOrEmpty(market))
            {
                if (market == "SH")
                {
                    fs = "m:1"; // 上交所
                }
                else if (market == "SZ")
                {
                    fs = "m:2"; // 深交所
                }
            }
            else
            {
                fs = "m:1+t:2"; // 全部A股（上交所+深交所）
            }
            
            int pageSize = 100; // 每页100只股票
            int pageNum = 1;
            int totalFetched = 0;
            
            // 字段说明：
            // f57: 代码, f58: 名称, f43: 最新价, f44: 最高价, f45: 最低价
            // f46: 今开, f60: 昨收, f47: 成交量, f48: 成交额
            // f170: 涨跌幅, f169: 涨跌额, f168: 换手率
            // f162: 市盈率(PE), f167: 市净率(PB)
            string fields = "f57,f58,f43,f44,f45,f46,f60,f47,f48,f170,f169,f168,f162,f167";
            
            while (totalFetched < maxCount)
            {
                // 东方财富股票列表API
                // pn: 页码, pz: 每页数量, po: 排序(1=降序), np: 1
                // fltt: 2, invt: 2, fid: f3(按涨跌幅排序), fs: 筛选条件
                // fields: 需要获取的字段
                var url = $"http://82.push2.eastmoney.com/api/qt/clist/get?" +
                    $"pn={pageNum}&pz={pageSize}&po=1&np=1&fltt=2&invt=2&fid=f3&fs={fs}&fields={fields}";
                
                _logger.LogDebug("请求东方财富股票列表，页码: {PageNum}, URL: {Url}", pageNum, url);
                
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                _httpClient.DefaultRequestHeaders.Add("Referer", "http://quote.eastmoney.com/");
                
                var response = await _httpClient.GetStringAsync(url);
                
                dynamic? data = Newtonsoft.Json.JsonConvert.DeserializeObject(response);
                
                if (data?.data == null || data.data.diff == null)
                {
                    _logger.LogWarning("东方财富返回数据为空，页码: {PageNum}", pageNum);
                    break;
                }
                
                var stocks = data.data.diff;
                int pageCount = 0;
                
                foreach (var stockInfo in stocks)
                {
                    try
                    {
                        // 获取股票代码（格式：1.600000 或 0.000001）
                        string? secid = stockInfo.f57?.ToString();
                        if (string.IsNullOrEmpty(secid))
                            continue;
                        
                        // 解析secid获取市场代码和股票代码
                        var parts = secid.Split('.');
                        if (parts.Length != 2)
                            continue;
                        
                        string marketCode = parts[0];
                        string stockCode = parts[1];
                        
                        // 判断市场
                        string marketType = marketCode == "1" ? "SH" : "SZ";
                        
                        // 只处理A股（排除B股等）
                        // B股: 90开头（上交所B股），20开头（深交所B股）
                        // 排除其他非A股代码
                        if (stockCode.StartsWith("90") || stockCode.StartsWith("20"))
                            continue;
                        
                        // clist接口的价格格式：通常直接是元，但某些字段可能需要除以100
                        // f43: 最新价（可能需要除以100）, f60: 昨收（可能需要除以100）
                        // 其他价格字段（f44,f45,f46）格式一致
                        decimal currentPriceRaw = SafeConvertToDecimal(stockInfo.f43);
                        decimal closePriceRaw = SafeConvertToDecimal(stockInfo.f60);
                        
                        // clist接口的价格通常需要除以100（单位是分）
                        decimal currentPrice = currentPriceRaw / 100;
                        decimal closePrice = closePriceRaw / 100;
                        decimal openPrice = SafeConvertToDecimal(stockInfo.f46) / 100;
                        decimal highPrice = SafeConvertToDecimal(stockInfo.f44) / 100;
                        decimal lowPrice = SafeConvertToDecimal(stockInfo.f45) / 100;
                        
                        // 价格回退逻辑：非交易时间使用昨收价
                        if (currentPrice == 0 && closePrice > 0)
                            currentPrice = closePrice;
                        if (openPrice == 0 && closePrice > 0)
                            openPrice = closePrice;
                        if (highPrice == 0)
                            highPrice = currentPrice;
                        if (lowPrice == 0)
                            lowPrice = currentPrice;
                        
                        // 获取PE和PB（可能为0、负数或"-"，需要处理）
                        decimal peValue = SafeConvertToDecimal(stockInfo.f162);
                        decimal pbValue = SafeConvertToDecimal(stockInfo.f167);
                        
                        var stock = new Stock
                        {
                            Code = stockCode,
                            Name = stockInfo.f58?.ToString() ?? "未知",
                            Market = marketType,
                            CurrentPrice = currentPrice,
                            OpenPrice = openPrice,
                            ClosePrice = closePrice,
                            HighPrice = highPrice,
                            LowPrice = lowPrice,
                            Volume = SafeConvertToDecimal(stockInfo.f47),
                            Turnover = SafeConvertToDecimal(stockInfo.f48),
                            ChangeAmount = SafeConvertToDecimal(stockInfo.f169) / 100, // 涨跌额也需要除以100
                            ChangePercent = SafeConvertToDecimal(stockInfo.f170),
                            TurnoverRate = SafeConvertToDecimal(stockInfo.f168),
                            PE = peValue > 0 ? peValue : null,
                            PB = pbValue > 0 ? pbValue : null,
                            LastUpdate = DateTime.Now
                        };
                        
                        // 跳过价格为0或无效的股票（可能是停牌、退市等）
                        if (stock.CurrentPrice <= 0 || string.IsNullOrEmpty(stock.Name) || stock.Name == "未知")
                            continue;
                        
                        allStocks.Add(stock);
                        pageCount++;
                        totalFetched++;
                        
                        if (totalFetched >= maxCount)
                            break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("解析股票数据失败: {Error}", ex.Message);
                        continue;
                    }
                }
                
                _logger.LogInformation("第 {PageNum} 页获取到 {Count} 只股票", pageNum, pageCount);
                
                // 如果这一页获取的股票数量少于页面大小，说明已经是最后一页
                if (pageCount < pageSize || totalFetched >= maxCount)
                    break;
                
                pageNum++;
                
                // 添加延迟，避免请求过快
                await Task.Delay(200);
            }
            
            _logger.LogInformation("从东方财富总共获取到 {Count} 只股票", allStocks.Count);
            return allStocks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从东方财富获取股票列表失败");
            return allStocks; // 返回已获取的部分数据
        }
    }
}


