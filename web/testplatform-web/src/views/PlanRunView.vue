<template>
  <div class="plan-run-layout">

    <!-- 顶部 -->
    <div class="run-header">
      <el-button text @click="$router.push(`/plans/${planId}`)">
        <el-icon><ArrowLeft /></el-icon> 返回计划
      </el-button>
      <div class="header-info">
        <span class="header-name">{{ planName }}</span>
        <el-tag :type="planRunTagType" size="large" effect="dark">{{ planRunLabel }}</el-tag>
      </div>
      <div class="header-stats">
        <span class="stat-pass">✓ {{ passedCount }}</span>
        <span class="stat-fail">✗ {{ failedCount }}</span>
        <span class="stat-pend">◌ {{ pendingCount }}</span>
        <span class="elapsed">{{ elapsed }}</span>
      </div>
      <el-button
        v-if="planRun?.status === 'running'"
        type="danger" size="small"
        @click="cancelRun"
        :loading="cancelling"
      >取消运行</el-button>
    </div>

    <!-- 主体：三列 -->
    <div class="run-body">

      <!-- 左列：场景列表 -->
      <div class="col-scenes">
        <div class="col-title"><el-icon><List /></el-icon> 场景列表</div>
        <div class="scene-list">
          <div
            v-for="(item, idx) in items"
            :key="item.id"
            class="scene-item"
            :class="{
              'scene-selected': selectedId === item.id,
              'scene-running':  item.status === 'running',
              'scene-passed':   item.status === 'passed',
              'scene-failed':   item.status === 'failed'
            }"
            @click="selectItem(item)"
          >
            <div class="scene-icon">
              <el-icon v-if="item.status === 'running'" class="spin" color="#e6a23c"><Loading /></el-icon>
              <el-icon v-else-if="item.status === 'passed'" color="#67c23a"><CircleCheck /></el-icon>
              <el-icon v-else-if="item.status === 'failed'" color="#f56c6c"><CircleClose /></el-icon>
              <span v-else class="seq-num">{{ idx + 1 }}</span>
            </div>
            <div class="scene-meta">
              <div class="scene-name">{{ item.scenarioName }}</div>
              <el-tag :type="item.scenarioType === 'web' ? 'success' : 'primary'" size="small" effect="plain">
                {{ item.scenarioType === 'web' ? '🌐 Web' : '🖥 WPF' }}
              </el-tag>
            </div>
          </div>
        </div>
      </div>

      <!-- 中列：测试步骤（解析当前场景描述） -->
      <div class="col-steps">
        <div class="col-title"><el-icon><Document /></el-icon> 测试步骤</div>
        <div class="steps-list">
          <template v-if="descLines.length">
            <div v-for="(line, i) in descLines" :key="i"
                 class="desc-step"
                 :class="{
                   'desc-header':  line.isHeader,
                   'desc-done':    line.stepNum !== null && isPassed,
                   'desc-running': line.stepNum !== null && isCurrentStep(line.stepNum)
                 }"
            >
              <template v-if="line.isHeader">
                <el-icon><FolderOpened /></el-icon>
                <span>{{ line.text }}</span>
              </template>
              <template v-else-if="line.stepNum !== null">
                <span class="desc-num"
                  :class="{ 'num-done': isPassed || isCurrentStep(line.stepNum), 'num-curr': isCurrentStep(line.stepNum) }"
                >{{ line.stepNum }}</span>
                <el-icon v-if="isPassed" color="#67c23a" style="flex-shrink:0"><CircleCheck /></el-icon>
                <el-icon v-else-if="isCurrentStep(line.stepNum)" color="#e6a23c" class="spin" style="flex-shrink:0"><Loading /></el-icon>
                <span class="desc-text">{{ line.text }}</span>
              </template>
              <template v-else>
                <span class="desc-plain">{{ line.text }}</span>
              </template>
            </div>
          </template>
          <div v-else class="steps-empty">点击左侧场景查看步骤</div>
        </div>
      </div>

      <!-- 右列：实时日志 -->
      <div class="col-logs">
        <div class="col-title">
          <el-icon><Monitor /></el-icon> 实时日志
          <span v-if="selectedItem?.status === 'running'" class="live-dot"></span>
          <span class="log-title-name">{{ selectedItem?.scenarioName }}</span>
        </div>

        <div v-if="!selectedItem" class="log-empty">
          <el-empty description="点击左侧场景查看日志" :image-size="60" />
        </div>

        <template v-else>
          <div v-if="selectedItem.status === 'running'" class="thinking-bar">
            <el-icon class="spin"><Loading /></el-icon> 场景执行中...
          </div>

          <!-- 滚动区域：只有日志列表滚动 -->
          <div ref="logContainer" class="log-list">
            <div v-if="!selectedItem.logs?.length" class="log-wait">
              <el-icon class="spin"><Loading /></el-icon>
              {{ selectedItem.status === 'pending' ? '等待执行...' : '加载日志...' }}
            </div>

            <div v-for="log in selectedItem.logs" :key="log.step"
                 style="background:#fff; border:1px solid #e4e7ed; border-radius:6px; overflow:hidden; margin-bottom:4px;"
                 :style="log.success === false ? { borderColor:'#fbc4c4' } : {}"
            >
              <div style="display:flex; align-items:center; justify-content:space-between; padding:8px 12px; cursor:pointer; user-select:none;"
                   @click="log.expanded = !log.expanded">
                <div style="display:flex; align-items:center; gap:8px; flex:1; min-width:0;">
                  <span style="font-size:11px; color:#c0c4cc; width:28px; flex-shrink:0;">#{{ log.step }}</span>
                  <el-tag size="small" :type="toolTagType(log.tool)" style="flex-shrink:0; font-size:11px;">{{ log.tool || 'system' }}</el-tag>
                  <span style="font-size:12px; color:#606266; overflow:hidden; text-overflow:ellipsis; white-space:nowrap;">{{ preview(log.result) }}</span>
                </div>
                <div style="display:flex; align-items:center; gap:8px; flex-shrink:0;">
                  <span :style="{ color: log.success !== false ? '#67c23a' : '#f56c6c', fontSize:'13px' }">{{ log.success !== false ? '✓' : '✗' }}</span>
                  <span style="font-size:11px; color:#c0c4cc;">{{ log.time }}</span>
                </div>
              </div>
              <div v-if="log.expanded" style="padding:8px 12px; border-top:1px solid #f0f0f0; background:#fafafa;">
                <div v-if="log.args && log.args !== '{}'">
                  <div style="font-size:10px; color:#909399; text-transform:uppercase; margin-bottom:3px;">参数</div>
                  <pre style="margin:0; font-size:12px; font-family:monospace; white-space:pre-wrap; word-break:break-all; background:#f8f8f8; padding:6px 8px; border-radius:4px; max-height:160px; overflow-y:auto;">{{ fmtJson(log.args) }}</pre>
                </div>
                <div style="margin-top:6px;">
                  <div style="font-size:10px; color:#909399; text-transform:uppercase; margin-bottom:3px;">结果</div>
                  <pre :style="{ margin:0, fontSize:'12px', fontFamily:'monospace', whiteSpace:'pre-wrap', wordBreak:'break-all', background: log.success !== false ? '#f8f8f8' : '#fff0f0', color: log.success !== false ? 'inherit' : '#f56c6c', padding:'6px 8px', borderRadius:'4px', maxHeight:'160px', overflowY:'auto' }">{{ log.result }}</pre>
                </div>
              </div>
            </div>
          </div>

          <!-- 底部固定按钮：在滚动区域外 -->
          <div v-if="selectedItem.testRunId"
               style="padding:8px 16px; border-top:1px solid #e4e7ed; background:#fff; text-align:right; flex-shrink:0;">
            <el-button text size="small" @click="$router.push(`/monitor/${selectedItem.testRunId}`)">
              查看完整日志 →
            </el-button>
          </div>
        </template>
      </div>

    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import {
  ArrowLeft, ArrowDown, Loading, CircleCheck, CircleClose,
  List, Document, Monitor, FolderOpened
} from '@element-plus/icons-vue'
import { planApi, taskApi, scenarioApi } from '../api'

const route  = useRoute()
const router = useRouter()
const planId = route.params.planId as string
const runId  = route.params.runId  as string

const planName   = ref('')
const planRun    = ref<any>(null)
const items      = ref<any[]>([])
const selectedId = ref('')
const cancelling = ref(false)
const logContainer = ref<HTMLElement>()
const startMs    = ref(Date.now())
const elapsed    = ref('0s')

// 当前选中场景的描述内容
const currentDescription = ref('')
const executedStepCount  = ref(0)

let pollTimer: any = null
let elapsedTimer: any = null

const selectedItem = computed(() => items.value.find(i => i.id === selectedId.value) ?? null)
const isPassed     = computed(() => selectedItem.value?.status === 'passed')

// ── 统计 ─────────────────────────────────────────────────
const passedCount  = computed(() => items.value.filter(i => i.status === 'passed').length)
const failedCount  = computed(() => items.value.filter(i => i.status === 'failed').length)
const pendingCount = computed(() => items.value.filter(i => i.status === 'pending' || i.status === 'running').length)

const planRunTagType = computed(() => {
  if (!planRun.value) return 'info'
  const m: Record<string,string> = { running:'warning', completed: failedCount.value ? 'danger' : 'success' }
  return m[planRun.value.status] || 'info'
})
const planRunLabel = computed(() => {
  if (!planRun.value) return '加载中'
  const m: Record<string,string> = { running:'运行中', completed: failedCount.value ? `${failedCount.value} 个失败` : '全部通过' }
  return m[planRun.value.status] || planRun.value.status
})

// ── 测试步骤解析（和 TaskMonitor 一致） ──────────────────
interface DescLine { text: string; isHeader: boolean; stepNum: number | null }
const descLines = computed<DescLine[]>(() => {
  if (!currentDescription.value) return []
  return currentDescription.value.split('\n')
    .map(l => l.trim()).filter(l => l.length > 0)
    .map(line => {
      if (line.startsWith('【') && line.includes('】'))
        return { text: line.replace('【','').replace('】',''), isHeader: true, stepNum: null }
      const m = line.match(/^(\d+)[.、。]\s*(.+)/)
      if (m) return { text: m[2], isHeader: false, stepNum: parseInt(m[1]) }
      return { text: line, isHeader: false, stepNum: null }
    })
})

const totalDescSteps = computed(() => descLines.value.filter(l => l.stepNum !== null).length || 1)

function isCurrentStep(stepNum: number): boolean {
  if (isPassed.value) return false
  if (selectedItem.value?.status !== 'running') return false
  const done = Math.floor(executedStepCount.value / Math.max(1, executedStepCount.value / totalDescSteps.value * 0.8 || 3))
  return stepNum === done + 1
}

// ── 取消运行 ──────────────────────────────────────────────
async function cancelRun() {
  cancelling.value = true
  try {
    await planApi.cancelRun(runId)
    ElMessage.info('已取消计划运行')
  } catch { ElMessage.error('取消失败') } finally { cancelling.value = false }
}

// ── 选中场景 + 加载描述 ───────────────────────────────────
async function selectItem(item: any) {
  selectedId.value = item.id
  executedStepCount.value = item.logs?.length ?? 0

  // 加载该场景描述
  if (item.scenarioId) {
    try {
      const res = await scenarioApi.get(item.scenarioId)
      currentDescription.value = res.data.description || ''
    } catch { currentDescription.value = '' }
  }

  if (item.testRunId && !item.logs?.length) await fetchLogs(item)
  await nextTick(); scrollToBottom()
}

async function fetchLogs(item: any) {
  if (!item.testRunId) return
  try {
    const res = await taskApi.logs(item.testRunId)
    // 归一化字段名（REST API 返回 camelCase，统一转成和 TaskMonitor 一样的格式）
    const logs = (res.data as any[]).map(l => ({
      step:     l.stepNumber,
      tool:     l.toolName   || l.tool   || '',
      args:     l.arguments  || l.args   || '{}',
      result:   l.result     || '',
      success:  l.success,
      time:     l.createdAt ? new Date(l.createdAt).toLocaleTimeString('zh-CN') : '',
      expanded: l.success === false   // 失败步骤默认展开
    }))
    // 用 ID 找到当前 reactive 对象，避免 stale 引用问题
    const current = items.value.find(i => i.id === item.id)
    if (current) {
      current.logs = logs
      executedStepCount.value = logs.length
    }
    await nextTick(); scrollToBottom()
  } catch (e) { console.error('fetchLogs failed:', e) }
}

function scrollToBottom() {
  if (logContainer.value)
    logContainer.value.scrollTop = logContainer.value.scrollHeight
}

// ── 轮询 ─────────────────────────────────────────────────
async function poll() {
  try {
    const [runRes, itemsRes] = await Promise.all([
      planApi.getRuns(planId).then((r: any) => (r.data as any[]).find(x => x.id === runId)),
      planApi.getRunItems(runId)
    ])
    planRun.value = runRes

    const prev = new Map(items.value.map((i: any) => [i.id, i]))
    const newItems: any[] = itemsRes.data

    for (const ni of newItems) {
      const old = prev.get(ni.id)
      if (old?.logs) ni.logs = old.logs

      if (ni.status === 'running' && old?.status !== 'running') {
        selectedId.value = ni.id
        // 加载场景描述
        if (ni.scenarioId) {
          scenarioApi.get(ni.scenarioId).then(r => {
            currentDescription.value = r.data.description || ''
          }).catch(() => {})
        }
        setTimeout(() => fetchLogs(ni), 300)
      }

      if ((ni.status === 'passed' || ni.status === 'failed') && old?.status === 'running') {
        ni.logs = null
        setTimeout(() => fetchLogs(ni), 500)
      }
    }

    items.value = newItems

    // 当前选中场景：running 时每轮刷新日志；completed 只拉一次最终日志
    const sel = newItems.find(i => i.id === selectedId.value)
    if (sel?.testRunId) {
      if (sel.status === 'running') fetchLogs(sel)           // 实时刷新
      else if (!sel.logs?.length)   fetchLogs(sel)           // 完成后拉一次
    }

    if (runRes?.status === 'completed') {
      clearInterval(pollTimer); clearInterval(elapsedTimer)
    }
  } catch { }
}

function updateElapsed() {
  const sec = Math.floor((Date.now() - startMs.value) / 1000)
  elapsed.value = sec < 60 ? `${sec}s` : `${Math.floor(sec/60)}m ${sec%60}s`
}

async function init() {
  const planRes = await planApi.get(planId)
  planName.value = planRes.data.name
  await poll()

  const running = items.value.find(i => i.status === 'running')
  const first   = items.value[0]
  const target  = running || first
  if (target) await selectItem(target)

  if (planRun.value?.startedAt) {
    const d = planRun.value.startedAt
    startMs.value = new Date(d.endsWith('Z') ? d : d + 'Z').getTime()
  }

  pollTimer    = setInterval(poll, 1500)   // 1.5秒轮询，实时感更强
  elapsedTimer = setInterval(updateElapsed, 1000)
  updateElapsed()
}

const preview     = (t: string) => (t || '').trim().slice(0, 80).replace(/\n/g, ' ')
const fmtTime     = (d: string) => d ? new Date(d).toLocaleTimeString('zh-CN') : ''
const fmtJson     = (s: string) => { try { return JSON.stringify(JSON.parse(s), null, 2) } catch { return s } }
const toolTagType = (t: string): string => {
  const m: Record<string,string> = { scan_ui:'', check_dialog:'info', click:'success', click_cell:'success',
    set_text:'warning', press_key:'info', wait:'info', done:'success', error:'danger',
    system:'info', auto_close_dialog:'warning', go_home:'info' }
  return m[t] ?? ''
}

onMounted(init)
onUnmounted(() => { clearInterval(pollTimer); clearInterval(elapsedTimer) })
</script>

<style scoped>
/* ── 整体 ── */
.plan-run-layout { display:flex; flex-direction:column; height:100%; overflow:hidden; background:#f5f7fa; }

/* ── 顶部 ── */
.run-header {
  display:flex; align-items:center; gap:14px; padding:10px 20px;
  background:#fff; border-bottom:1px solid #e4e7ed; flex-shrink:0; flex-wrap:wrap;
}
.header-info  { flex:1; display:flex; align-items:center; gap:10px; }
.header-name  { font-family: var(--font-serif); font-size:18px; font-weight:600; color:var(--app-ink); }
.header-stats { display:flex; align-items:center; gap:12px; font-size:14px; font-weight:600; }
.stat-pass { color:#67c23a; }
.stat-fail { color:#f56c6c; }
.stat-pend { color:#909399; }
.elapsed   { font-size:13px; color:#606266; font-weight:400; }

/* ── 主体三列 ── */
.run-body { display:flex; flex:1; min-height:0; overflow:hidden; }

/* ── 公共列标题 ── */
.col-title {
  padding:11px 14px; font-size:12px; font-weight:600; color:#606266;
  border-bottom:1px solid #f0f0f0; flex-shrink:0;
  display:flex; align-items:center; gap:6px; background:#fafafa;
  text-transform:uppercase; letter-spacing:.3px;
}
.live-dot { width:7px;height:7px;border-radius:50%;background:#e6a23c;animation:blink 1s infinite;margin-left:4px; }
.log-title-name { font-size:12px; color:#909399; text-transform:none; margin-left:4px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
@keyframes blink { 0%,100%{opacity:1}50%{opacity:.2} }

/* ── 左列：场景列表 ── */
.col-scenes {
  width:200px; flex-shrink:0; background:#fff;
  border-right:1px solid #e4e7ed;
  display:flex; flex-direction:column; overflow:hidden;
}
.scene-list { flex:1; overflow-y:auto; }
.scene-list::-webkit-scrollbar { width:4px; }
.scene-list::-webkit-scrollbar-thumb { background:#e4e7ed; border-radius:2px; }

.scene-item {
  display:flex; align-items:center; gap:8px; padding:10px 12px;
  cursor:pointer; border-bottom:1px solid #f5f5f5;
  border-left:3px solid transparent; transition:all .15s;
}
.scene-item:hover  { background:#f5f7fa; }
.scene-selected    { background:#f0eee7 !important; border-left-color:var(--app-ink); }
.scene-running     { border-left-color:#e6a23c; }
.scene-passed      { border-left-color:#67c23a; }
.scene-failed      { border-left-color:#f56c6c; }
.scene-icon  { width:20px; flex-shrink:0; display:flex; justify-content:center; }
.seq-num     { font-size:11px; color:#c0c4cc; font-weight:600; }
.scene-meta  { flex:1; min-width:0; }
.scene-name  { font-size:12px; color:#303133; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; margin-bottom:3px; }

/* ── 中列：测试步骤 ── */
.col-steps {
  width:260px; flex-shrink:0; background:#fff;
  border-right:1px solid #e4e7ed;
  display:flex; flex-direction:column; overflow:hidden;
}
.steps-list { flex:1; overflow-y:auto; padding:6px 0; }
.steps-list::-webkit-scrollbar { width:4px; }
.steps-list::-webkit-scrollbar-thumb { background:#e4e7ed; border-radius:2px; }
.steps-empty { padding:24px; color:#c0c4cc; font-size:13px; text-align:center; }

.desc-step  { display:flex; align-items:flex-start; gap:8px; padding:6px 14px; font-size:12px; line-height:1.5; }
.desc-header{ font-weight:600; color:#303133; background:#f5f7fa; padding:7px 14px; margin:2px 0; }
.desc-done  { color:#67c23a; }

.desc-num {
  width:18px; height:18px; border-radius:50%;
  background:#e4e7ed; color:#606266; font-size:10px;
  display:flex; align-items:center; justify-content:center; flex-shrink:0; margin-top:2px;
  transition:all .3s;
}
.num-done { background:#67c23a !important; color:#fff !important; }
.num-curr { background:#e6a23c !important; color:#fff !important; }
.desc-text  { color:#606266; flex:1; }
.desc-plain { color:#909399; font-size:11px; padding-left:26px; }

/* ── 右列：实时日志 ── */
.col-logs {
  flex:1; min-width:0; min-height:0;
  display:flex; flex-direction:column;
  overflow:hidden;    /* 必须有，和 TaskMonitor 的 right-panel 一致 */
}
.log-empty  { flex:1; display:flex; align-items:center; justify-content:center; }
.thinking-bar {
  padding:7px 14px; background:#fff8e6; border-bottom:1px solid #faecd8;
  font-size:12px; color:#b88230; display:flex; align-items:center; gap:6px; flex-shrink:0;
}
.log-list {
  flex:1;
  min-height:0;
  overflow-y:scroll;
  padding:10px 14px;
  /* 不用 display:flex，用普通块布局，gap 改成 margin-bottom */
}
.log-list::-webkit-scrollbar       { width:8px; }
.log-list::-webkit-scrollbar-track { background:#f5f7fa; }
.log-list::-webkit-scrollbar-thumb { background:#c0c4cc; border-radius:4px; }
.log-list::-webkit-scrollbar-thumb:hover { background:#909399; }

.log-wait { padding:30px; display:flex; align-items:center; justify-content:center; gap:8px; color:#909399; font-size:13px; }

.log-card     { background:#fff; border:1px solid #e4e7ed; border-radius:6px; overflow:hidden; }
.log-card-err { border-color:#fbc4c4; }

.log-head     { display:flex; align-items:center; justify-content:space-between; padding:7px 10px; cursor:pointer; }
.log-head:hover { background:#f5f7fa; }
.log-head-l   { display:flex; align-items:center; gap:7px; flex:1; min-width:0; }
.log-head-r   { display:flex; align-items:center; gap:7px; flex-shrink:0; }
.log-idx      { font-size:11px; color:#c0c4cc; width:26px; flex-shrink:0; }
.log-tool     { flex-shrink:0; font-size:11px; }
.log-preview  { font-size:12px; color:#606266; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
.log-time     { font-size:11px; color:#c0c4cc; }
.ok { color:#67c23a; font-size:13px; }
.err{ color:#f56c6c; font-size:13px; }

.log-body { padding:8px 10px; border-top:1px solid #f0f0f0; background:#fafafa; display:flex; flex-direction:column; gap:6px; }
.log-sec  { }
.sec-label{ font-size:10px; color:#909399; text-transform:uppercase; letter-spacing:.5px; margin-bottom:3px; }
.log-pre  {
  margin:0; font-size:12px; font-family:monospace; white-space:pre-wrap; word-break:break-all;
  background:#f8f8f8; padding:6px 8px; border-radius:4px; max-height:160px; overflow-y:auto;
}
.log-pre::-webkit-scrollbar { width:4px; }
.log-pre::-webkit-scrollbar-thumb { background:#e4e7ed; border-radius:2px; }
.log-pre-err { background:#fff0f0; color:#f56c6c; }
.view-full { padding:8px 0; text-align:right; border-top:1px solid #f0f0f0; margin-top:4px; }

.spin { animation:spin 1s linear infinite; }
@keyframes spin { to { transform:rotate(360deg); } }
</style>
