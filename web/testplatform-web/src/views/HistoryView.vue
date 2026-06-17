<template>
  <div class="page page--table">
    <div class="page-head">
      <div>
        <h2 class="page-title">执行历史</h2>
        <div class="page-subtitle">
          {{ dim === 'plan' ? '按测试计划查看：计划内每个场景的最近测试状态'
            : dim === 'scenario' ? '按场景查看：该场景的每一次运行记录'
            : '全部运行记录：计划批量与单独场景的执行结果' }}
        </div>
      </div>
      <div class="head-controls">
        <el-radio-group v-model="dim" @change="onDimChange" size="default">
          <el-radio-button value="all">全部</el-radio-button>
          <el-radio-button value="plan">测试计划</el-radio-button>
          <el-radio-button value="scenario">单个场景</el-radio-button>
        </el-radio-group>

        <!-- 计划维度：选计划 -->
        <el-select v-if="dim === 'plan'" v-model="selPlanId" placeholder="选择测试计划"
                   filterable style="width: 200px;" @change="loadPlanStatus">
          <el-option v-for="p in plans" :key="p.id" :label="p.name" :value="p.id" />
        </el-select>
        <template v-if="dim === 'plan' && selPlanId">
          <el-button @click="$router.push(`/plans/${selPlanId}`)">进入计划</el-button>
          <el-button @click="$router.push(`/plans/${selPlanId}/history`)">批量执行历史</el-button>
        </template>

        <!-- 场景维度：选场景 -->
        <el-select v-if="dim === 'scenario'" v-model="filterScenarioId" placeholder="选择场景"
                   clearable filterable style="width: 220px;" @change="loadHistory">
          <el-option v-for="s in scenarios" :key="s.id" :label="s.name" :value="s.id" />
        </el-select>

        <!-- 状态过滤（非计划维度）-->
        <el-select v-if="dim !== 'plan'" v-model="filterStatus" placeholder="全部状态" clearable style="width: 120px;">
          <el-option label="通过" value="passed" />
          <el-option label="失败" value="failed" />
          <el-option label="运行中" value="running" />
          <el-option label="已取消" value="cancelled" />
        </el-select>
        <el-date-picker v-if="dim !== 'plan'" v-model="dateRange" type="daterange"
                        value-format="YYYY-MM-DD" range-separator="至"
                        start-placeholder="开始日期" end-placeholder="结束日期"
                        style="width: 250px;" @change="loadHistory" />

        <el-button :loading="loading" @click="refresh">刷新</el-button>
        <el-tag v-if="hasRunning" type="warning" size="small" effect="plain">运行中 · 自动刷新</el-tag>
      </div>
    </div>

    <!-- 统计卡片 -->
    <div class="stat-row">
      <div v-for="(c, i) in statCards" :key="i" class="stat-card" :class="c.cls">
        <div class="stat-num">{{ c.num }}</div>
        <div class="stat-label">{{ c.label }}</div>
      </div>
    </div>

    <!-- ① 计划维度：计划内场景的最近状态 -->
    <div v-if="dim === 'plan'" class="table-wrap">
      <el-table :data="planRows" v-loading="loading" class="data-card" height="100%">
        <el-table-column type="index" label="#" width="56" align="center" />
        <el-table-column prop="name" label="场景名称" min-width="200" show-overflow-tooltip />
        <el-table-column label="执行方式" width="140" align="center">
          <template #default="{ row }">
            <el-tag v-if="row.stepCount > 0" type="success" size="small" effect="plain">录制 {{ row.stepCount }} 步</el-tag>
            <el-tag v-else type="warning" size="small" effect="plain">AI 推理</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="最近状态" width="120" align="center">
          <template #default="{ row }">
            <el-tag v-if="row.lastStatus" :type="statusType(row.lastStatus)" size="small"
                    :effect="row.lastRunId ? 'plain' : 'light'"
                    :style="row.lastRunId ? 'cursor:pointer' : ''"
                    @click="row.lastRunId && $router.push(`/monitor/${row.lastRunId}`)">
              {{ statusLabel(row.lastStatus) }}
            </el-tag>
            <span v-else class="muted">未运行</span>
          </template>
        </el-table-column>
        <el-table-column label="最近运行" width="170">
          <template #default="{ row }">{{ row.lastStartedAt ? formatDate(row.lastStartedAt) : '—' }}</template>
        </el-table-column>
        <el-table-column label="步数" width="80" align="center">
          <template #default="{ row }">{{ row.lastSteps != null ? row.lastSteps : '—' }}</template>
        </el-table-column>
        <el-table-column label="操作" width="180" fixed="right">
          <template #default="{ row }">
            <template v-if="row.lastRunId">
              <el-button size="small" @click="viewLogs({ id: row.lastRunId })">日志</el-button>
              <el-button size="small" @click="$router.push(`/monitor/${row.lastRunId}`)">详情</el-button>
            </template>
            <span v-else class="muted" style="font-size:12px;">尚未运行</span>
          </template>
        </el-table-column>
      </el-table>
    </div>

    <!-- ② 全部 / 单个场景维度：运行记录 -->
    <div v-else class="table-wrap">
      <el-table :data="pagedHistory" v-loading="loading" class="data-card" height="100%">
        <el-table-column label="开始时间" width="170">
          <template #default="{ row }">{{ formatDate(row.startedAt) }}</template>
        </el-table-column>
        <el-table-column label="场景" min-width="180" show-overflow-tooltip>
          <template #default="{ row }">{{ getScenarioName(row.scenarioId) }}</template>
        </el-table-column>
        <el-table-column label="状态" width="100" align="center">
          <template #default="{ row }">
            <el-tag :type="statusType(row.status)" size="small" effect="plain">{{ statusLabel(row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="totalSteps" label="步数" width="70" align="center" />
        <el-table-column label="Token" width="90" align="center">
          <template #default="{ row }">{{ row.tokenUsed > 0 ? row.tokenUsed.toLocaleString() : '-' }}</template>
        </el-table-column>
        <el-table-column label="耗时" width="90">
          <template #default="{ row }">{{ calcDuration(row.startedAt, row.finishedAt) }}</template>
        </el-table-column>
        <el-table-column label="错误信息" min-width="160" show-overflow-tooltip>
          <template #default="{ row }"><span class="err">{{ row.errorMsg }}</span></template>
        </el-table-column>
        <el-table-column label="操作" width="230" fixed="right">
          <template #default="{ row }">
            <el-button v-if="row.status === 'running'" size="small" type="warning"
                       @click="$router.push(`/monitor/${row.id}`)">实时查看</el-button>
            <template v-else>
              <el-button size="small" @click="viewLogs(row)">日志</el-button>
              <el-button size="small" @click="$router.push(`/monitor/${row.id}`)">详情</el-button>
              <el-button size="small" type="success" @click="rerun(row)" :loading="rerunningId === row.id">再次运行</el-button>
            </template>
          </template>
        </el-table-column>
      </el-table>
    </div>

    <!-- 分页（全部 / 单个场景维度）-->
    <div v-if="dim !== 'plan' && filteredHistory.length > pageSize" class="pager-row">
      <el-pagination background layout="total, sizes, prev, pager, next"
                     :total="filteredHistory.length"
                     v-model:current-page="currentPage" v-model:page-size="pageSize"
                     :page-sizes="[20, 50, 100, 200]" />
    </div>

    <!-- 日志弹窗 -->
    <el-dialog v-model="logDialogVisible" title="执行日志" width="820px" destroy-on-close>
      <div v-loading="logsLoading" class="log-box">
        <div v-for="(log, i) in runLogs" :key="i" class="log-line" :class="{ 'log-line-err': !log.success }">
          <span class="log-time">[{{ formatDate(log.createdAt) }}]</span>
          <span class="log-step">Step {{ log.stepNumber }}</span>
          <el-tag size="small" :type="!log.success ? 'danger' : 'success'" effect="plain" style="margin-right:8px;">{{ log.toolName }}</el-tag>
          <span v-if="log.arguments" class="log-args">{{ log.arguments }}</span>
          <span :class="!log.success ? 'err' : 'ok'">{{ log.result }}</span>
          <div v-if="log.thinking" class="log-think">AI 思考：{{ log.thinking }}</div>
        </div>
        <div v-if="runLogs.length === 0 && !logsLoading" class="log-empty">暂无日志</div>
      </div>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { useRoute } from 'vue-router'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { scenarioApi, taskApi, planApi } from '../api'

const route  = useRoute()
const router = useRouter()

type Dim = 'all' | 'plan' | 'scenario'
const dim = ref<Dim>((route.query.scenarioId ? 'scenario' : 'all'))

const history = ref<any[]>([])
const scenarios = ref<any[]>([])
const plans = ref<any[]>([])
const planRows = ref<any[]>([])
const loading = ref(false)
const filterScenarioId = ref<string>((route.query.scenarioId as string) || '')
const filterStatus = ref<string>('')
const dateRange = ref<[string, string] | null>(null)
const currentPage = ref(1)
const pageSize = ref(20)
const selPlanId = ref<string>('')
const logDialogVisible = ref(false)
const runLogs = ref<any[]>([])
const logsLoading = ref(false)
const rerunningId = ref<string>('')

let refreshTimer: ReturnType<typeof setInterval> | null = null

const filteredHistory = computed(() =>
  filterStatus.value ? history.value.filter(r => r.status === filterStatus.value) : history.value
)
const pagedHistory = computed(() => {
  const start = (currentPage.value - 1) * pageSize.value
  return filteredHistory.value.slice(start, start + pageSize.value)
})
// 筛选或数据变化时回到第一页
watch([filterStatus, history], () => { currentPage.value = 1 })

const hasRunning = computed(() =>
  dim.value === 'plan'
    ? planRows.value.some(r => r.lastStatus === 'running')
    : history.value.some(r => r.status === 'running')
)

function countByStatus(status: string) {
  return filteredHistory.value.filter(r => r.status === status).length
}

const passRate = computed(() => {
  const finished = filteredHistory.value.filter(r => r.status === 'passed' || r.status === 'failed')
  if (finished.length === 0) return '-'
  return Math.round(countByStatus('passed') / finished.length * 100) + '%'
})

const statCards = computed(() => {
  if (dim.value === 'plan') {
    const passed = planRows.value.filter(r => r.lastStatus === 'passed').length
    const failed = planRows.value.filter(r => r.lastStatus === 'failed').length
    const notRun = planRows.value.filter(r => !r.lastStatus).length
    return [
      { num: planRows.value.length, label: '场景数' },
      { num: passed, label: '通过', cls: 'stat-pass' },
      { num: failed, label: '失败', cls: 'stat-fail' },
      { num: notRun, label: '未运行' }
    ]
  }
  return [
    { num: filteredHistory.value.length, label: '总次数' },
    { num: countByStatus('passed'), label: '通过', cls: 'stat-pass' },
    { num: countByStatus('failed'), label: '失败', cls: 'stat-fail' },
    { num: passRate.value, label: '通过率' }
  ]
})

function formatDate(dateStr: string) {
  if (!dateStr) return '-'
  return new Date(dateStr).toLocaleString('zh-CN')
}
function statusType(s: string) {
  const map: Record<string, any> = { running: 'warning', passed: 'success', failed: 'danger', cancelled: 'info' }
  return map[s] || 'info'
}
function statusLabel(s: string) {
  const map: Record<string, string> = { running: '运行中', passed: '通过', failed: '失败', cancelled: '已取消', pending: '等待中' }
  return map[s] || s
}
function getScenarioName(id: string) {
  const s = scenarios.value.find(s => s.id === id)
  return s ? s.name : (id?.substring(0, 8) + '...')
}
function calcDuration(start: string, end: string) {
  if (!start || !end) return '-'
  const sec = Math.floor((new Date(end).getTime() - new Date(start).getTime()) / 1000)
  if (sec < 60) return `${sec}s`
  return `${Math.floor(sec / 60)}m ${sec % 60}s`
}

async function loadHistory() {
  loading.value = true
  try {
    const sid = dim.value === 'scenario' ? (filterScenarioId.value || undefined) : undefined
    const res = await taskApi.history({
      scenarioId: sid,
      from: dateRange.value?.[0] ? dateRange.value[0] + 'T00:00:00' : undefined,
      to:   dateRange.value?.[1] ? dateRange.value[1] + 'T23:59:59' : undefined
    })
    history.value = res.data
  } catch {
    ElMessage.error('加载历史失败')
  } finally {
    loading.value = false
  }
}

async function loadPlanStatus() {
  if (!selPlanId.value) { planRows.value = []; return }
  loading.value = true
  try {
    planRows.value = (await planApi.scenarioStatus(selPlanId.value)).data
  } catch {
    ElMessage.error('加载计划场景状态失败')
  } finally {
    loading.value = false
  }
}

function refresh() {
  if (dim.value === 'plan') loadPlanStatus()
  else loadHistory()
}

function onDimChange() {
  if (dim.value === 'plan') {
    if (!selPlanId.value && plans.value.length) selPlanId.value = plans.value[0].id
    loadPlanStatus()
  } else {
    loadHistory()
  }
}

async function rerun(row: any) {
  rerunningId.value = row.id
  try {
    let inputParams: Record<string, string> = {}
    try { inputParams = JSON.parse(row.inputParamsJson || '{}') } catch { }
    const res = await taskApi.run(row.scenarioId, inputParams, 'auto')
    ElMessage.success('已开始运行')
    router.push(`/monitor/${res.data.runId}`)
  } catch (e: any) {
    ElMessage.error(e.response?.data || '启动失败')
  } finally {
    rerunningId.value = ''
  }
}

async function viewLogs(row: any) {
  logDialogVisible.value = true
  runLogs.value = []
  logsLoading.value = true
  try {
    const res = await taskApi.logs(row.id)
    runLogs.value = res.data
  } catch {
    ElMessage.error('加载日志失败')
  } finally {
    logsLoading.value = false
  }
}

onMounted(async () => {
  try { scenarios.value = (await scenarioApi.list()).data } catch {}
  try { plans.value = (await planApi.list()).data } catch {}
  onDimChange()
  // 有运行中任务时每 5 秒自动刷新当前维度
  refreshTimer = setInterval(() => { if (hasRunning.value) refresh() }, 5000)
})

onUnmounted(() => { if (refreshTimer) clearInterval(refreshTimer) })
</script>

<style scoped>
.head-controls { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
.pager-row { display: flex; justify-content: flex-end; padding: 12px 4px 0; flex-shrink: 0; }

.data-card {
  background: var(--app-surface);
  border-radius: 2px;
  border: 1px solid var(--line);
  padding: 4px 16px;
}
.stat-row { display: flex; gap: 12px; margin-bottom: 16px; flex-shrink: 0; }
.stat-card {
  background: var(--app-surface);
  border-radius: 2px;
  border: 1px solid var(--line);
  padding: 18px 30px;
  text-align: center;
  min-width: 116px;
  transition: border-color .2s ease;
}
.stat-card:hover { border-color: var(--app-ink); }
.stat-num   { font-family: var(--font-serif); font-size: 30px; font-weight: 600; color: var(--app-ink); line-height: 1.05; }
.stat-label {
  font-family: var(--font-mono); font-size: 10.5px; color: var(--app-text-sub);
  margin-top: 6px; text-transform: uppercase; letter-spacing: 1px;
}
.stat-pass .stat-num { color: var(--el-color-success); }
.stat-fail .stat-num { color: var(--el-color-danger); }

.muted { color: var(--app-text-mut); font-size: 13px; }
.err { color: var(--el-color-danger); }
.ok  { color: var(--el-color-success); }

/* 日志弹窗 */
.log-box { max-height: 540px; overflow-y: auto; font-size: 13px; }
.log-line { padding: 8px 12px; border-bottom: 1px solid var(--line); line-height: 1.6; }
.log-line-err { background: #fbf1f0; }
.log-time { color: var(--app-text-mut); margin-right: 10px; font-family: var(--font-mono); font-size: 12px; }
.log-step { font-family: var(--font-mono); color: var(--app-ink); margin-right: 8px; }
.log-args { color: var(--app-text-sub); margin-right: 8px; white-space: pre-wrap; }
.log-think {
  margin-top: 6px; font-size: 12.5px; color: var(--app-text-sub);
  background: #faf9f5; border-left: 3px solid var(--el-color-warning); padding: 6px 10px;
}
.log-empty { text-align: center; color: var(--app-text-mut); padding: 40px; }
</style>
