<template>
  <div class="monitor-layout">

    <!-- ── 顶部状态栏 ── -->
    <div class="monitor-header">
      <el-button text @click="$router.back()">
        <el-icon><ArrowLeft /></el-icon> 返回
      </el-button>

      <div class="header-info">
        <span class="scenario-name">{{ scenarioName }}</span>
        <el-tag :type="statusType" size="large" effect="plain" class="status-tag">{{ statusLabel }}</el-tag>
      </div>

      <div class="header-stats">
        <div class="stat-item">
          <el-icon><Clock /></el-icon>
          <span>{{ elapsed }}</span>
        </div>
        <div class="stat-item">
          <el-icon><List /></el-icon>
          <span>{{ steps.length }} 步</span>
        </div>
        <div class="stat-item" v-if="failedCount > 0" style="color: #f56c6c;">
          <el-icon><CircleClose /></el-icon>
          <span>{{ failedCount }} 步失败</span>
        </div>
      </div>

      <el-button
        v-if="status === 'running'"
        type="danger"
        @click="cancelRun"
        :loading="cancelling"
      >
        <el-icon><VideoPause /></el-icon> 取消
      </el-button>
      <el-button
        v-else
        type="primary"
        @click="rerun"
        :loading="rerunning"
      >
        <el-icon><RefreshRight /></el-icon> 再次运行
      </el-button>
    </div>

    <!-- ── 主体：左侧描述 + 右侧步骤流 ── -->
    <div class="monitor-body">

      <!-- 左侧：测试步骤（结构化按录制步骤精确高亮）+ 验证条件结果 -->
      <div class="left-panel">
        <div class="panel-title">
          <el-icon><Document /></el-icon> 测试步骤
        </div>
        <div class="desc-steps">
          <el-empty v-if="sideSteps.length === 0" description="无步骤信息" :image-size="60" />
          <div
            v-for="s in sideSteps"
            :key="s.num"
            class="side-step"
            :class="`side-${stepState(s.num)}`"
          >
            <span class="side-num" :class="`num-${stepState(s.num)}`">{{ s.num }}</span>
            <el-icon v-if="stepState(s.num) === 'done'" color="#67c23a" style="flex-shrink:0"><CircleCheck /></el-icon>
            <el-icon v-else-if="stepState(s.num) === 'fail'" color="#f56c6c" style="flex-shrink:0"><CircleClose /></el-icon>
            <el-icon v-else-if="stepState(s.num) === 'current'" color="#e6a23c" class="spin" style="flex-shrink:0"><Loading /></el-icon>
            <span class="side-text">{{ s.text }}</span>
          </div>
        </div>

        <!-- 验证条件结果 -->
        <div v-if="assertResults.length" class="assert-box">
          <div class="assert-title">验证条件 {{ assertPassCount }}/{{ assertResults.length }}</div>
          <div
            v-for="(a, i) in assertResults"
            :key="i"
            class="assert-item"
            :class="a.ok ? 'assert-ok' : 'assert-fail'"
          >
            <el-icon v-if="a.ok" color="#67c23a"><CircleCheck /></el-icon>
            <el-icon v-else color="#f56c6c"><CircleClose /></el-icon>
            <span>{{ a.text }}</span>
          </div>
        </div>

        <!-- AI 截图验证结论 -->
        <div v-if="aiVerdict" class="assert-box">
          <div class="assert-title">🤖 AI 截图验证</div>
          <div class="assert-item" :class="aiVerdict.ok ? 'assert-ok' : 'assert-fail'">
            <el-icon v-if="aiVerdict.ok" color="#67c23a"><CircleCheck /></el-icon>
            <el-icon v-else color="#f56c6c"><CircleClose /></el-icon>
            <span style="white-space:pre-wrap;">{{ aiVerdict.text }}</span>
          </div>
        </div>
      </div>

      <!-- 右侧：实时步骤日志 -->
      <div class="right-panel">
        <div class="panel-title">
          <el-icon><Monitor /></el-icon> 实时日志
          <span class="live-dot" v-if="status === 'running'"></span>
        </div>

        <!-- AI 正在思考提示 -->
        <div v-if="thinkingText && status === 'running'" class="thinking-bar">
          <el-icon class="spin"><Loading /></el-icon>
          <span>{{ thinkingText }}</span>
        </div>

        <div ref="logContainer" class="log-container">
          <div v-if="steps.length === 0" class="log-empty">
            <el-icon><Loading /></el-icon> 等待执行开始...
          </div>

          <div
            v-for="(s, i) in steps"
            :key="i"
            class="step-card"
            :class="{ 'step-card-error': !s.success, 'step-card-running': s.running }"
          >
            <!-- 步骤头（始终显示，点击展开） -->
            <div class="step-head" @click="s.expanded = !s.expanded">
              <div class="step-head-left">
                <span class="step-index">#{{ s.step }}</span>
                <el-tag
                  size="small"
                  :type="toolTagType(s.tool)"
                  class="tool-tag"
                >{{ s.tool }}</el-tag>
                <span class="step-preview">{{ s.resultPreview }}</span>
              </div>
              <div class="step-head-right">
                <span v-if="s.running" class="step-status-running">
                  <el-icon class="spin"><Loading /></el-icon>
                </span>
                <span v-else-if="s.success" class="step-status-ok">✓</span>
                <span v-else class="step-status-err">✗</span>
                <span class="step-time">{{ s.time }}</span>
                <el-icon :class="s.expanded ? 'arrow-up' : 'arrow-down'">
                  <ArrowDown />
                </el-icon>
              </div>
            </div>

            <!-- 展开内容 -->
            <div v-if="s.expanded" class="step-body">
              <!-- AI 思考 -->
              <div v-if="s.thinking" class="step-section">
                <div class="step-section-label">
                  <el-icon><ChatDotSquare /></el-icon> AI 思考
                </div>
                <div class="step-thinking">{{ s.thinking }}</div>
              </div>

              <!-- 参数 -->
              <div v-if="s.args && s.args !== '{}'" class="step-section">
                <div class="step-section-label">
                  <el-icon><Setting /></el-icon> 参数
                </div>
                <pre class="step-code">{{ formatJson(s.args) }}</pre>
              </div>

              <!-- 结果 -->
              <div class="step-section">
                <div class="step-section-label">
                  <el-icon><Tickets /></el-icon> 结果
                </div>
                <pre
                  class="step-result"
                  :class="{ 'step-result-error': !s.success }"
                >{{ s.result }}</pre>
              </div>
            </div>
          </div>

          <!-- 结束摘要 -->
          <div v-if="summary && status !== 'running'" class="summary-card" :class="`summary-${status}`">
            <el-icon v-if="status === 'passed'"><CircleCheck /></el-icon>
            <el-icon v-else><CircleClose /></el-icon>
            <span>{{ summary }}</span>
            <span class="summary-duration">总耗时 {{ elapsed }}</span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import {
  ArrowLeft, ArrowDown, Clock, List, Document, Monitor,
  CircleCheck, CircleClose, Loading,
  ChatDotSquare, Setting, Tickets, VideoPause, RefreshRight
} from '@element-plus/icons-vue'
import * as signalR from '@microsoft/signalr'
import { taskApi, scenarioApi } from '../api'

// ── 路由参数 ─────────────────────────────────────────────
const route  = useRoute()
const router = useRouter()
const runId  = route.params.runId as string

// ── 状态 ─────────────────────────────────────────────────
const steps        = ref<any[]>([])
const status       = ref('running')
const scenarioName = ref('')
const scenarioDesc = ref('')
const startTime    = ref<number>(0)        // 从服务端读取，不用 Date.now()
const endTime      = ref<number | null>(null)
const elapsed      = ref('...')
const cancelling   = ref(false)
const rerunning    = ref(false)
const runInfo      = ref<any>(null)        // 本次运行记录（scenarioId / inputParamsJson）

// 失败步数（排除 system 提示类日志）
const failedCount = computed(() =>
  steps.value.filter((s: any) => !s.success && !['system', 'done'].includes(s.tool)).length
)
const logContainer = ref<HTMLElement>()
const thinkingText = ref('')
const summary      = ref('')

// 最终是否通过
const isPassed = ref(false)

// 录制步骤（结构化场景）；每个步骤号的执行结果；验证条件结果；AI 验证结论
const playSteps    = ref<any[]>([])
const stepResults  = ref<Record<number, boolean>>({})
const assertResults = ref<{ ok: boolean; text: string }[]>([])
const aiVerdict    = ref<{ ok: boolean; text: string } | null>(null)

let connection: signalR.HubConnection | null = null
let timer: ReturnType<typeof setInterval> | null = null
let pollTimer: ReturnType<typeof setInterval> | null = null

// ── 计算属性 ──────────────────────────────────────────────
const statusType = computed(() => ({
  running: 'warning', passed: 'success', failed: 'danger',
  cancelled: 'info', pending: 'info'
}[status.value] || 'info'))

const statusLabel = computed(() => ({
  running: '运行中', passed: '通过', failed: '失败',
  cancelled: '已取消', pending: '等待中'
}[status.value] || status.value))

// ── 左侧步骤列表 ──────────────────────────────────────────
// 结构化场景：用录制步骤；否则回退到描述里的数字步骤行（AI 场景）
interface SideStep { num: number; text: string }

const sideSteps = computed<SideStep[]>(() => {
  if (playSteps.value.length > 0)
    return playSteps.value.map((s, i) => ({ num: i + 1, text: stepText(s) }))
  // AI 回退：解析描述中的 "1. xxx" 行
  if (!scenarioDesc.value) return []
  const out: SideStep[] = []
  for (const raw of scenarioDesc.value.split('\n')) {
    const m = raw.trim().match(/^(\d+)[.、。]\s*(.+)/)
    if (m) out.push({ num: parseInt(m[1]), text: m[2] })
  }
  return out
})

const assertPassCount = computed(() => assertResults.value.filter(a => a.ok).length)

// 已执行到的最大步骤号 +1 = 即将执行的步骤（用于高亮“当前”）
const nextStep = computed(() => {
  const done = Object.keys(stepResults.value).map(Number)
  return done.length ? Math.max(...done) + 1 : 1
})

// 每个步骤号的状态：done / fail / current / pending（严格按步骤号，单调推进不乱跳）
function stepState(num: number): 'done' | 'fail' | 'current' | 'pending' {
  if (num in stepResults.value) return stepResults.value[num] ? 'done' : 'fail'
  if (isPassed.value) return 'done'
  if (status.value === 'running' && num === nextStep.value) return 'current'
  return 'pending'
}

function stepText(s: any): string {
  const name = s.targetName || s.target
  switch (s.action) {
    case 'set_text':    return `在「${name}」输入「${s.value}」`
    case 'select_item': return `在「${name}」选择「${s.value}」`
    case 'click_cell':  return `点击表格第${s.value}行「${s.targetName}」`
    case 'press_key':   return `按键 ${s.value}`
    case 'wait':        return `等待 ${s.value}ms`
    default:            return `点击「${name}」`
  }
}

// ── 工具图标颜色 ─────────────────────────────────────────
function toolTagType(tool: string) {
  const map: Record<string, string> = {
    scan_ui: '',       check_dialog: 'info',   click: 'success',
    click_cell: 'success', set_text: 'warning',   set_cell: 'warning',
    set_unit_price: 'warning', select_item: 'warning', read_value: '',
    get_row_count: 'info', press_key: 'info',    wait: 'info',
    screenshot: 'info', done: 'success',        error: 'danger',
    assert: 'warning', ai_verify: 'warning',     go_home: 'info',
    system: 'info'
  }
  return map[tool] ?? ''
}

// ── JSON 格式化 ───────────────────────────────────────────
function formatJson(str: string) {
  try { return JSON.stringify(JSON.parse(str), null, 2) }
  catch { return str }
}

// ── 耗时格式化 ────────────────────────────────────────────
function formatElapsed(sec: number): string {
  if (sec < 0) sec = 0
  const h = Math.floor(sec / 3600)
  const m = Math.floor((sec % 3600) / 60)
  const s = sec % 60
  if (h > 0) return `${h}h ${m}m ${s}s`
  if (m > 0) return `${m}m ${s}s`
  return `${s}s`
}

// ── 耗时计算 ──────────────────────────────────────────────
// pageOpenTime：页面打开时刻，用于"运行中"的实时计时（避免时区误差）
const pageOpenTime = Date.now()

function updateElapsed() {
  if (endTime.value) {
    // 已结束：用服务端 finishedAt - startedAt（秒级精度，避免时区问题）
    const sec = Math.max(0, Math.round((endTime.value - startTime.value) / 1000))
    elapsed.value = formatElapsed(sec)
  } else {
    // 运行中：从页面打开时刻开始计（不依赖服务端时区）
    const sec = Math.floor((Date.now() - pageOpenTime) / 1000)
    elapsed.value = formatElapsed(sec)
  }
}

function startTimer() {
  updateElapsed()
  timer = setInterval(updateElapsed, 1000)
}

// ── 滚动到底部 ────────────────────────────────────────────
async function scrollToBottom() {
  await nextTick()
  if (logContainer.value)
    logContainer.value.scrollTop = logContainer.value.scrollHeight
}

// ── 加载初始数据 ──────────────────────────────────────────
async function loadInitialData() {
  try {
    const [statusRes, logsRes] = await Promise.all([
      taskApi.status(runId),
      taskApi.logs(runId)
    ])
    const run = statusRes.data
    runInfo.value   = run
    status.value    = run.status
    startTime.value = toLocalMs(run.startedAt)
    if (run.finishedAt) {
      endTime.value  = toLocalMs(run.finishedAt)
      isPassed.value = run.status === 'passed'
      updateElapsed()   // 已完成：立即算出正确耗时
    }
    // 运行中：不调 updateElapsed，等 startTimer 里的 pageOpenTime 计时

    // 加载场景信息（描述 + 录制步骤，用于左侧步骤列表）
    try {
      const scRes = await scenarioApi.get(run.scenarioId)
      scenarioName.value = scRes.data.name
      scenarioDesc.value = scRes.data.description
      try { playSteps.value = JSON.parse(scRes.data.stepsJson || '[]') } catch { playSteps.value = [] }
    } catch {
      scenarioName.value = run.scenarioId?.substring(0, 8) + '...'
    }

    // 把历史日志转为步骤卡片
    for (const log of logsRes.data) {
      pushStep({
        step:    log.stepNumber,
        tool:    log.toolName,
        args:    log.arguments,
        result:  log.result,
        success: log.success,
        time:    new Date(log.createdAt).toLocaleTimeString('zh-CN'),
        thinking: log.thinking || ''
      })
    }
    scrollToBottom()
  } catch {
    ElMessage.error('加载运行状态失败')
  }
}

// ── UTC 时间字符串转本地毫秒（修复时区问题）────────────────
function toLocalMs(dateStr: string): number {
  if (!dateStr) return Date.now()
  // 确保被当作 UTC 解析（PostgreSQL 返回的不带 Z）
  const s = dateStr.endsWith('Z') || dateStr.includes('+') ? dateStr : dateStr + 'Z'
  return new Date(s).getTime()
}

// ── 添加步骤卡片 ──────────────────────────────────────────
function pushStep(data: any) {
  // trim 掉开头换行，避免 preview 显示为空
  const resultStr = (data.result || '').trim()
  const preview = resultStr.length > 60
    ? resultStr.substring(0, 60) + '...'
    : resultStr

  steps.value.push({
    step:    data.step,
    tool:    data.tool,
    args:    data.args || '{}',
    result:  data.result,
    resultPreview: preview,
    success: data.success !== false,
    time:    data.time || '',
    thinking: data.thinking || '',
    running:  false,
    expanded: false
  })

  // 错误步骤自动展开
  const last = steps.value[steps.value.length - 1]
  if (!last.success) last.expanded = true

  // 左侧步骤进度：结构化回放里 step 号与录制步骤一一对应，直接据此标记（单调推进不乱跳）
  if (!['system', 'error', 'assert', 'go_home', 'check_dialog', 'auto_close_dialog'].includes(data.tool)
      && data.step > 0 && data.step <= sideSteps.value.length) {
    stepResults.value[data.step] = data.success !== false
  }

  // 验证条件结果（后端 assert 日志）
  if (data.tool === 'assert') {
    assertResults.value.push({
      ok: data.success !== false,
      text: (data.result || '').replace(/^[✓✗]\s*(验证通过|验证失败):\s*/, '')
    })
  }

  // AI 截图验证结论
  if (data.tool === 'ai_verify') {
    aiVerdict.value = {
      ok: data.success !== false,
      text: (data.result || '').replace(/^[✓✗⊘]\s*AI[^:：]*[:：]\s*/, '')
    }
  }
}

// ── SignalR 连接 ──────────────────────────────────────────
async function connectSignalR() {
  connection = new signalR.HubConnectionBuilder()
    .withUrl('http://localhost:5000/hubs/test')
    .withAutomaticReconnect()
    .build()

  connection.on('StepLog', (data) => {
    thinkingText.value = ''   // 清除思考状态
    pushStep(data)
    scrollToBottom()
  })

  // 新增：AI 思考中事件（暂时用 StepLog 里 tool='thinking' 区分）
  connection.on('Thinking', (text: string) => {
    thinkingText.value = text
  })

  connection.on('RunFinished', (data) => {
    status.value       = data.status
    isPassed.value     = data.status === 'passed'
    summary.value      = data.summary || (data.status === 'passed' ? '✅ 测试通过' : '❌ 测试失败')
    thinkingText.value = ''
    endTime.value = data.finishedAt ? toLocalMs(data.finishedAt) : Date.now()
    updateElapsed()
    if (timer) clearInterval(timer)

    ElMessage({
      type: data.status === 'passed' ? 'success' : (data.status === 'cancelled' ? 'warning' : 'error'),
      message: `运行${statusLabel.value}`
    })
    scrollToBottom()
  })

  try {
    await connection.start()
    await connection.invoke('JoinRun', runId)
  } catch (e) {
    console.error('SignalR 连接失败', e)
  }
}

// ── 再次运行（用相同参数重跑一次）──────────────────────────
async function rerun() {
  if (!runInfo.value) return
  rerunning.value = true
  try {
    let inputParams: Record<string, string> = {}
    try { inputParams = JSON.parse(runInfo.value.inputParamsJson || '{}') } catch { }
    const res = await taskApi.run(runInfo.value.scenarioId, inputParams, 'auto')
    ElMessage.success('已开始运行')
    // router-view 带 :key，跳转后组件会重新挂载
    router.push(`/monitor/${res.data.runId}`)
  } catch (e: any) {
    ElMessage.error(e.response?.data || '启动失败')
  } finally {
    rerunning.value = false
  }
}

// ── 取消运行 ─────────────────────────────────────────────
async function cancelRun() {
  cancelling.value = true
  try {
    await taskApi.cancel(runId)
    ElMessage.info('已发送取消请求')
  } catch {
    ElMessage.error('取消失败')
  } finally {
    cancelling.value = false
  }
}

// ── 生命周期 ──────────────────────────────────────────────
onMounted(async () => {
  await loadInitialData()
  if (status.value === 'running') {
    startTimer()
    await connectSignalR()
    // 兜底轮询：每5秒检查一次状态，防止 SignalR RunFinished 丢失
    pollTimer = setInterval(async () => {
      try {
        const res = await taskApi.status(runId)
        const s = res.data.status
        if (s !== 'running') {
          status.value  = s
          isPassed.value = s === 'passed'
          endTime.value  = toLocalMs(res.data.finishedAt || new Date().toISOString())
          updateElapsed()
          if (timer) clearInterval(timer)
          if (pollTimer) clearInterval(pollTimer)
          summary.value = s === 'passed' ? '✅ 测试通过' : '❌ 测试失败'
        }
      } catch { /* 忽略 */ }
    }, 5000)
  }
})

onUnmounted(async () => {
  if (timer) clearInterval(timer)
  if (pollTimer) clearInterval(pollTimer)
  if (connection) {
    try { await connection.invoke('LeaveRun', runId) } catch {}
    await connection.stop()
  }
})
</script>

<style scoped>
/* ── 整体布局 ── */
.monitor-layout {
  display: flex;
  flex-direction: column;
  height: 100%;          /* 继承父级，不用 100vh（否则算不进左侧栏） */
  min-height: 0;
  background: #f5f7fa;
  overflow: hidden;
}

/* ── 顶部栏 ── */
.monitor-header {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 12px 24px;
  background: #fff;
  border-bottom: 1px solid #e4e7ed;
  flex-shrink: 0;
  flex-wrap: wrap;
}
.header-info {
  display: flex;
  align-items: center;
  gap: 10px;
  flex: 1;
}
.scenario-name { font-weight: 600; font-size: 15px; }
/* 状态徽标：主题把 el-tag 背景透明化了，这里按状态给回浅底，保证文字可见 */
.status-tag { font-weight: 600; }
.status-tag.el-tag--success { background: #eef5f0 !important; color: var(--el-color-success) !important; border-color: var(--el-color-success) !important; }
.status-tag.el-tag--danger  { background: #f8eeed !important; color: var(--el-color-danger)  !important; border-color: var(--el-color-danger)  !important; }
.status-tag.el-tag--warning { background: #f7f0e4 !important; color: var(--el-color-warning) !important; border-color: var(--el-color-warning) !important; }
.status-tag.el-tag--info    { background: #f0f0ee !important; color: var(--el-color-info)    !important; border-color: var(--el-color-info)    !important; }
.header-stats {
  display: flex;
  gap: 20px;
  color: #606266;
  font-size: 14px;
}
.stat-item { display: flex; align-items: center; gap: 4px; }

/* ── 主体 ── */
.monitor-body {
  display: flex;
  flex: 1;
  min-height: 0;      /* 关键：让 flex 子元素可以收缩 */
  overflow: hidden;
  gap: 0;
}

/* ── 左侧描述 ── */
.left-panel {
  width: 300px;
  flex-shrink: 0;
  min-height: 0;      /* 同上，必须加 */
  background: #fff;
  border-right: 1px solid #e4e7ed;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.panel-title {
  padding: 12px 16px;
  font-family: var(--font-mono);
  font-weight: 400;
  font-size: 11px;
  letter-spacing: 1.2px;
  text-transform: uppercase;
  color: var(--app-text-sub);
  border-bottom: 1px solid var(--line);
  display: flex;
  align-items: center;
  gap: 6px;
  background: #faf9f5;
  flex-shrink: 0;
}
.desc-steps {
  flex: 1;
  overflow-y: auto;
  padding: 8px 0;
}
.side-step {
  padding: 6px 16px;
  font-size: 13px;
  display: flex;
  align-items: flex-start;
  gap: 8px;
  transition: background 0.2s;
}
.side-current { background: #fdf6ec; }
.side-fail    { background: #fef0f0; }
.side-num {
  width: 20px;
  height: 20px;
  border-radius: 50%;
  background: #e4e7ed;
  color: #606266;
  font-size: 11px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  margin-top: 1px;
  transition: background 0.3s, color 0.3s;
}
.num-done    { background: #67c23a !important; color: #fff !important; }
.num-current { background: #e6a23c !important; color: #fff !important; }
.num-fail    { background: #f56c6c !important; color: #fff !important; }
.side-text   { color: #606266; line-height: 1.5; }

/* 验证条件结果 */
.assert-box {
  border-top: 1px solid #f0f0f0;
  padding: 10px 16px;
  flex-shrink: 0;
  max-height: 40%;
  overflow-y: auto;
  background: #fafafa;
}
.assert-title {
  font-size: 12px;
  font-weight: 600;
  color: #909399;
  margin-bottom: 8px;
}
.assert-item {
  display: flex;
  align-items: flex-start;
  gap: 6px;
  font-size: 12px;
  line-height: 1.5;
  padding: 3px 0;
}
.assert-ok   { color: #67c23a; }
.assert-fail { color: #f56c6c; }

/* ── 右侧日志 ── */
.right-panel {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  min-height: 0;
}
.live-dot {
  width: 8px; height: 8px;
  border-radius: 50%;
  background: #e6a23c;
  animation: blink 1s infinite;
  margin-left: 6px;
}
@keyframes blink { 0%,100% { opacity: 1; } 50% { opacity: 0.2; } }

/* 思考提示条 */
.thinking-bar {
  padding: 8px 16px;
  background: #fff8e6;
  border-bottom: 1px solid #faecd8;
  font-size: 13px;
  color: #b88230;
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}

.log-container {
  /* height: 0 + flex: 1 是让 flex 子元素可滚动最可靠的写法 */
  flex: 1;
  height: 0;
  overflow-y: auto;
  padding: 12px 8px 12px 12px;
}
/* 始终显示滚动条，不跳动 */
.log-container::-webkit-scrollbar       { width: 8px; }
.log-container::-webkit-scrollbar-track { background: #ebeef5; border-radius: 4px; }
.log-container::-webkit-scrollbar-thumb { background: #c0c4cc; border-radius: 4px; }
.log-container::-webkit-scrollbar-thumb:hover { background: #909399; }
.log-empty {
  text-align: center;
  color: #999;
  padding: 40px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
}

/* ── 步骤卡片 ── */
.step-card {
  background: var(--app-surface);
  border: 1px solid var(--line);
  border-radius: 2px;
  overflow: hidden;
  transition: border-color 0.2s;
}
.step-card:hover { border-color: var(--app-ink); }
.step-card-error { border-color: #fbc4c4; }
.step-card-running { border-color: #f5dab1; }

.step-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 12px;
  cursor: pointer;
  user-select: none;
}
.step-head:hover { background: #f5f7fa; }
.step-head-left {
  display: flex;
  align-items: center;
  gap: 8px;
  flex: 1;
  min-width: 0;
}
.step-index {
  font-size: 11px;
  color: #909399;
  width: 28px;
  flex-shrink: 0;
}
.tool-tag { flex-shrink: 0; font-size: 11px; }
.step-preview {
  color: #606266;
  font-size: 12px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.step-head-right {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}
.step-status-ok  { color: #67c23a; font-size: 14px; }
.step-status-err { color: #f56c6c; font-size: 14px; }
.step-status-running { color: #e6a23c; }
.step-time { font-size: 11px; color: #c0c4cc; }
.arrow-up, .arrow-down { color: #c0c4cc; font-size: 12px; }
.arrow-up { transform: rotate(180deg); }

/* ── 展开内容 ── */
.step-body {
  border-top: 1px solid #f0f0f0;
  padding: 10px 12px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  background: #fafafa;
}
.step-section {}
.step-section-label {
  font-size: 11px;
  color: #909399;
  margin-bottom: 4px;
  display: flex;
  align-items: center;
  gap: 4px;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}
.step-thinking {
  font-size: 13px;
  color: #606266;
  line-height: 1.6;
  background: #fff8e6;
  padding: 8px;
  border-radius: 4px;
  border-left: 3px solid #e6a23c;
}
.step-code, .step-result {
  margin: 0;
  font-size: 12px;
  font-family: 'Consolas', 'Monaco', monospace;
  white-space: pre-wrap;
  word-break: break-all;
  background: #f8f8f8;
  padding: 8px;
  border-radius: 4px;
  max-height: 160px;
  overflow-y: auto;
}
.step-code::-webkit-scrollbar, .step-result::-webkit-scrollbar { width: 6px; }
.step-code::-webkit-scrollbar-thumb, .step-result::-webkit-scrollbar-thumb { background: #c0c4cc; border-radius: 3px; }
.step-result-error { background: #fff0f0; color: #f56c6c; }

/* ── 结束摘要 ── */
.summary-card {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 14px 16px;
  border-radius: 6px;
  font-size: 14px;
  font-weight: 500;
  margin-top: 8px;
}
.summary-passed   { background: #f0f9eb; color: #67c23a; border: 1px solid #c2e7b0; }
.summary-failed   { background: #fff0f0; color: #f56c6c; border: 1px solid #fbc4c4; }
.summary-cancelled{ background: #f4f4f5; color: #909399; border: 1px solid #d3d4d6; }
.summary-duration { margin-left: auto; font-size: 12px; color: #909399; }

/* ── 动画 ── */
.spin { animation: spin 1s linear infinite; }
@keyframes spin { from { transform: rotate(0deg); } to { transform: rotate(360deg); } }
</style>
