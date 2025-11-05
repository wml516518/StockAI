import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath, URL } from 'node:url'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        timeout: 600000, // 10分钟超时（AI分析可能需要较长时间）
        configure: (proxy, options) => {
          proxy.on('error', (err, req, res) => {
            console.error('代理错误:', err.message);
            console.error('请确保后端服务运行在 http://localhost:5000');
            if (res && !res.headersSent) {
              res.writeHead(500, {
                'Content-Type': 'application/json'
              });
              res.end(JSON.stringify({ 
                error: '代理错误', 
                message: err.message,
                hint: '请确保后端服务已启动'
              }));
            }
          });
          proxy.on('proxyReq', (proxyReq, req, res) => {
            console.log('代理请求:', req.method, req.url);
            // 对于AI分析请求，设置更长的超时
            if (req.url.includes('/ai/analyze')) {
              proxyReq.setTimeout(600000); // 10分钟
              console.log('AI分析请求，设置超时为10分钟');
            }
          });
          proxy.on('proxyRes', (proxyRes, req, res) => {
            console.log('代理响应:', req.url, '状态码:', proxyRes.statusCode);
            // 记录响应大小
            const contentLength = proxyRes.headers['content-length'];
            if (contentLength) {
              const sizeMB = (parseInt(contentLength) / 1024 / 1024).toFixed(2);
              console.log('响应大小:', sizeMB, 'MB');
            }
          });
        }
      }
    }
  },
  build: {
    outDir: '../src/StockAnalyse.Api/wwwroot',
    emptyOutDir: true
  }
})

