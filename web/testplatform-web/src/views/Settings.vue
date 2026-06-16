<template>
  <div class="page page--fixed-head">
    <!-- 固定页眉 -->
    <div class="page-head">
      <div>
        <h2 class="page-title">设置</h2>
        <div class="page-subtitle">配置默认测试目标与两套 AI 接口。修改后立即生效（存数据库，密钥加密保存）</div>
      </div>
      <el-button type="primary" :loading="saving" @click="save">保存配置</el-button>
    </div>

    <!-- 可滚动内容区 -->
    <div class="page-scroll" v-loading="loading">
      <div class="settings-wrap">

        <!-- ① 默认测试目标 -->
        <section class="cfg-card">
          <div class="cfg-head">
            <span class="cfg-no">01</span>
            <div>
              <h3 class="cfg-title">默认测试目标</h3>
              <p class="cfg-desc">新建场景 / 录制时默认指向的窗口（每个场景仍可单独修改）。从当前运行的窗口里选，或手动输入标题关键字。</p>
            </div>
          </div>
          <el-form label-position="top" class="cfg-form">
            <el-form-item label="默认目标窗口（WPF 桌面）">
              <div class="picker-row">
                <el-select v-model="form['Target:DefaultWindow']" filterable allow-create default-first-option
                           clearable placeholder="选择或输入窗口标题，如 SmartZaiko" style="flex:1;">
                  <el-option v-for="w in windows" :key="w.pid + '_' + w.title"
                             :label="w.title + (w.processName ? '  ·  ' + w.processName : '')" :value="w.title" />
                </el-select>
                <el-button :loading="winLoading" @click="loadWindows">刷新窗口</el-button>
              </div>
            </el-form-item>
            <p class="cfg-hint">
              仅 WPF 桌面场景使用；Web 场景的目标是「起始 URL」，在场景里单独填写。
              <span v-if="windows.length">当前检测到 {{ windows.length }} 个窗口。</span>
            </p>
          </el-form>
        </section>

        <!-- ② 操作 API：DeepSeek，纯文本推理，无需识图 -->
        <section class="cfg-card">
          <div class="cfg-head">
            <span class="cfg-no">02</span>
            <div>
              <h3 class="cfg-title">操作 API · AI 推理执行</h3>
              <p class="cfg-desc">驱动 AI 自主操作界面（tool-calling）。纯文本模型即可，<b>无需识图</b>。默认 DeepSeek。</p>
            </div>
          </div>
          <el-form label-position="top" class="cfg-form">
            <el-form-item label="API Key">
              <el-input v-model="form['DeepSeek:ApiKey']" type="password" autocomplete="off"
                        :placeholder="configured['DeepSeek:ApiKey'] ? '已配置（留空则保持不变）' : '未配置，请输入 sk-...'" />
            </el-form-item>
            <div class="cfg-row">
              <el-form-item label="模型">
                <el-select v-model="form['DeepSeek:Model']" filterable allow-create default-first-option
                           clearable placeholder="deepseek-chat" style="width:100%;">
                  <el-option v-for="m in deepseekModels" :key="m" :label="m" :value="m" />
                </el-select>
              </el-form-item>
              <el-form-item label="Base URL">
                <el-select v-model="form['DeepSeek:BaseUrl']" filterable allow-create default-first-option
                           clearable placeholder="https://api.deepseek.com" style="width:100%;">
                  <el-option v-for="u in deepseekUrls" :key="u" :label="u" :value="u" />
                </el-select>
              </el-form-item>
            </div>
          </el-form>
        </section>

        <!-- ③ 验证 API：多模态，必须识图 -->
        <section class="cfg-card">
          <div class="cfg-head">
            <span class="cfg-no">03</span>
            <div>
              <h3 class="cfg-title">验证 API · AI 截图验证</h3>
              <p class="cfg-desc">测试结束后对结果界面截图判读是否成功。<b class="warn">必须使用支持图像输入（多模态 / 识图）的模型</b>，否则无法读图。</p>
            </div>
          </div>
          <el-form label-position="top" class="cfg-form">
            <el-form-item label="API Key">
              <el-input v-model="form['AiVision:ApiKey']" type="password" autocomplete="off"
                        :placeholder="configured['AiVision:ApiKey'] ? '已配置（留空则保持不变）' : '未配置，请输入多模态模型的 Key'" />
            </el-form-item>
            <div class="cfg-row">
              <el-form-item label="模型（需支持识图）">
                <el-select v-model="form['AiVision:Model']" filterable allow-create default-first-option
                           clearable placeholder="如 glm-4v-flash / qwen-vl-plus / gpt-4o" style="width:100%;"
                           @change="onVisionModelChange">
                  <el-option v-for="m in visionModels" :key="m.value" :label="m.label" :value="m.value" />
                </el-select>
              </el-form-item>
              <el-form-item label="Base URL">
                <el-select v-model="form['AiVision:BaseUrl']" filterable allow-create default-first-option
                           clearable placeholder="如 https://open.bigmodel.cn/api/paas/v4" style="width:100%;">
                  <el-option v-for="u in visionUrls" :key="u.value" :label="u.label" :value="u.value" />
                </el-select>
              </el-form-item>
            </div>
            <p class="cfg-hint">
              走 OpenAI 兼容的 <span class="mono">/v1/chat/completions</span> 多模态消息格式。
              推荐 <span class="mono">glm-4v-flash</span>（智谱 · 便宜/限免）或 <span class="mono">qwen-vl-plus</span>（通义 · 低价）。
              选模型会自动带出对应 Base URL，可再手改。
            </p>
          </el-form>
        </section>

        <!-- ④ 日志清理 -->
        <section class="cfg-card">
          <div class="cfg-head">
            <span class="cfg-no">04</span>
            <div>
              <h3 class="cfg-title">日志清理</h3>
              <p class="cfg-desc">AI 调用（请求 / 响应 / 判定）会写入服务端日志文件，便于事后排查。超过保留天数的日志每天自动清理。</p>
            </div>
          </div>
          <el-form label-position="top" class="cfg-form">
            <el-form-item label="日志保留天数">
              <div class="picker-row">
                <el-input-number v-model="retentionDays" :min="1" :max="365" :step="1"
                                 controls-position="right" style="width:160px;" />
                <el-button :loading="cleaning" @click="cleanupNow">立即清理</el-button>
              </div>
            </el-form-item>
            <p class="cfg-hint">
              <template v-if="logInfo">
                当前日志：{{ logInfo.count }} 个文件 · {{ formatSize(logInfo.sizeKb) }} ·
                目录 <span class="mono">{{ logInfo.dir }}</span>
              </template>
              <template v-else>日志用量加载中…（需后端运行）</template>
              <br>「保存配置」后保留天数生效；后台每 24 小时按此清理一次。
            </p>
          </el-form>
        </section>

        <p class="foot-note">
          安全：两个 API Key <b>加密存储、不回显明文</b>；留空表示保持原密钥不变。其余项留空则回退
          <span class="mono">appsettings.json</span> 的默认值。
        </p>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { settingsApi, systemApi } from '../api'

const loading = ref(false)
const saving  = ref(false)
const winLoading = ref(false)
const windows = ref<{ title: string; processName: string; pid: number }[]>([])

// 日志清理
const retentionDays = ref(14)
const cleaning = ref(false)
const logInfo = ref<{ dir: string; count: number; sizeKb: number } | null>(null)

async function loadLogInfo() {
  try { logInfo.value = (await systemApi.logsInfo()).data } catch { logInfo.value = null }
}
function formatSize(kb: number) {
  return kb < 1024 ? `${kb} KB` : `${(kb / 1024).toFixed(1)} MB`
}
async function cleanupNow() {
  cleaning.value = true
  try {
    const res = await systemApi.logsCleanup(retentionDays.value)
    ElMessage.success(`已清理 ${res.data.deleted} 个过期日志`)
    await loadLogInfo()
  } catch {
    ElMessage.error('清理失败（请确认后端已启动）')
  } finally {
    cleaning.value = false
  }
}

// 键名与后端 / appsettings.json 一致
const form = reactive<Record<string, string>>({
  'DeepSeek:ApiKey': '', 'DeepSeek:Model': '', 'DeepSeek:BaseUrl': '',
  'AiVision:ApiKey': '', 'AiVision:Model': '', 'AiVision:BaseUrl': '',
  'Target:DefaultWindow': ''
})
// 敏感项是否已配置（用于占位提示，不回传密钥）
const configured = reactive<Record<string, boolean>>({
  'DeepSeek:ApiKey': false, 'AiVision:ApiKey': false
})

// ── 预设（下拉默认项，仍可手动输入）──
const deepseekModels = ['deepseek-chat', 'deepseek-reasoner']
const deepseekUrls   = ['https://api.deepseek.com']
const visionModels = [
  { value: 'glm-4v-flash', label: 'glm-4v-flash（智谱 · 便宜/限免）' },
  { value: 'glm-4v',       label: 'glm-4v（智谱）' },
  { value: 'qwen-vl-plus', label: 'qwen-vl-plus（通义 · 低价）' },
  { value: 'qwen-vl-max',  label: 'qwen-vl-max（通义 · 更强）' },
  { value: 'gpt-4o',       label: 'gpt-4o（OpenAI）' },
  { value: 'gpt-4o-mini',  label: 'gpt-4o-mini（OpenAI · 便宜）' }
]
const visionUrls = [
  { value: 'https://open.bigmodel.cn/api/paas/v4',           label: 'https://open.bigmodel.cn/api/paas/v4（智谱）' },
  { value: 'https://dashscope.aliyuncs.com/compatible-mode/v1', label: 'https://dashscope.aliyuncs.com/compatible-mode/v1（通义）' },
  { value: 'https://api.openai.com/v1',                      label: 'https://api.openai.com/v1（OpenAI）' }
]
// 模型 → 默认 BaseURL（选模型时若 URL 还空，自动带出）
const visionModelToUrl: Record<string, string> = {
  'glm-4v-flash': 'https://open.bigmodel.cn/api/paas/v4',
  'glm-4v':       'https://open.bigmodel.cn/api/paas/v4',
  'qwen-vl-plus': 'https://dashscope.aliyuncs.com/compatible-mode/v1',
  'qwen-vl-max':  'https://dashscope.aliyuncs.com/compatible-mode/v1',
  'gpt-4o':       'https://api.openai.com/v1',
  'gpt-4o-mini':  'https://api.openai.com/v1'
}
function onVisionModelChange(model: string) {
  if (!form['AiVision:BaseUrl'] && visionModelToUrl[model])
    form['AiVision:BaseUrl'] = visionModelToUrl[model]
}

async function loadWindows() {
  winLoading.value = true
  try {
    windows.value = (await systemApi.windows()).data
  } catch {
    ElMessage.error('获取窗口列表失败（请确认后端已启动）')
  } finally {
    winLoading.value = false
  }
}

async function load() {
  loading.value = true
  try {
    const res = await settingsApi.get()
    const v = res.data.values || {}
    const c = res.data.configured || {}
    // 非敏感项回填明文
    form['DeepSeek:Model']      = v['DeepSeek:Model']      ?? ''
    form['DeepSeek:BaseUrl']    = v['DeepSeek:BaseUrl']    ?? ''
    form['AiVision:Model']      = v['AiVision:Model']      ?? ''
    form['AiVision:BaseUrl']    = v['AiVision:BaseUrl']    ?? ''
    form['Target:DefaultWindow']= v['Target:DefaultWindow']?? ''
    retentionDays.value = parseInt(v['Logs:RetentionDays']) || 14
    // 敏感项不回填，仅标记是否已配置
    form['DeepSeek:ApiKey'] = ''
    form['AiVision:ApiKey'] = ''
    configured['DeepSeek:ApiKey'] = !!c['DeepSeek:ApiKey']
    configured['AiVision:ApiKey'] = !!c['AiVision:ApiKey']
  } catch {
    ElMessage.error('加载配置失败')
  } finally {
    loading.value = false
  }
}

async function save() {
  saving.value = true
  try {
    await settingsApi.save({ ...form, 'Logs:RetentionDays': String(retentionDays.value) })  // 敏感项留空 → 后端保持原值不变
    ElMessage.success('已保存，下次运行立即生效')
    await load()                          // 刷新「已配置」状态，并清空密钥输入框
  } catch {
    ElMessage.error('保存失败')
  } finally {
    saving.value = false
  }
}

onMounted(() => { load(); loadWindows(); loadLogInfo() })
</script>

<style scoped>
.settings-wrap { max-width: 900px; padding-bottom: 24px; }

.cfg-card {
  background: var(--app-surface);
  border: 1px solid var(--line);
  border-radius: 2px;
  padding: 26px 30px 14px;
  margin-bottom: 22px;
}

.cfg-head {
  display: flex;
  gap: 16px;
  align-items: flex-start;
  padding-bottom: 18px;
  margin-bottom: 20px;
  border-bottom: 1px solid var(--line);
}
.cfg-no {
  font-family: var(--font-mono);
  font-size: 13px;
  color: var(--app-text-mut);
  padding-top: 4px;
  letter-spacing: .5px;
}
.cfg-title {
  margin: 0;
  font-family: var(--font-serif);
  font-size: 21px;
  font-weight: 600;
  color: var(--app-ink);
}
.cfg-desc {
  margin: 6px 0 0;
  font-size: 13px;
  color: var(--app-text-sub);
  line-height: 1.6;
  max-width: 60ch;
}
.cfg-desc .warn { color: var(--el-color-warning); }

.cfg-form { margin-top: 2px; }
.picker-row { display: flex; gap: 10px; width: 100%; }
.cfg-row {
  display: grid;
  grid-template-columns: 1fr 1.4fr;
  gap: 18px;
}

.cfg-hint {
  font-size: 12px;
  color: var(--app-text-sub);
  line-height: 1.7;
  margin: 2px 0 6px;
}
.mono {
  font-family: var(--font-mono);
  font-size: .92em;
  background: var(--app-bg);
  border: 1px solid var(--line);
  border-radius: 2px;
  padding: 1px 5px;
}
.foot-note {
  font-size: 12.5px;
  color: var(--app-text-mut);
  line-height: 1.7;
  border-top: 1px solid var(--line);
  padding-top: 16px;
}
</style>
