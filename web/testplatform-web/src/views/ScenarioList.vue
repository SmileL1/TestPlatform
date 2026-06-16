<template>
  <div class="page page--table">
    <div class="page-head">
      <div>
        <h2 class="page-title">测试场景</h2>
        <div class="page-subtitle">录制或编写场景，支持结构化回放与 AI 推理执行</div>
      </div>
      <div style="display: flex; gap: 10px; align-items: center;">
        <el-input v-model="searchText" placeholder="搜索场景名称" clearable style="width: 200px;">
          <template #prefix><el-icon><Search /></el-icon></template>
        </el-input>
        <el-select v-model="filterType" style="width: 130px;">
          <el-option label="全部类型" value="" />
          <el-option label="🖥 WPF 桌面" value="wpf" />
          <el-option label="🌐 Web 网页" value="web" />
        </el-select>
        <el-button type="primary" @click="$router.push('/scenarios/new')">
          <el-icon><Plus /></el-icon> 新建场景
        </el-button>
      </div>
    </div>

    <div class="table-wrap">
    <el-table :data="filteredScenarios" v-loading="loading" stripe class="data-card" height="100%">
      <el-table-column prop="name" label="场景名称" min-width="180" />
      <el-table-column label="执行方式" width="130" align="center">
        <template #default="{ row }">
          <el-tag v-if="stepCount(row) > 0" type="success" size="small">
            🎯 录制 {{ stepCount(row) }} 步
          </el-tag>
          <el-tag v-else type="warning" size="small">🤖 AI 推理</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="最近运行" width="110" align="center">
        <template #default="{ row }">
          <el-tag
            v-if="lastRun(row.id)"
            :type="statusType(lastRun(row.id).status)"
            size="small"
            style="cursor: pointer;"
            @click="$router.push(`/monitor/${lastRun(row.id).id}`)"
          >
            {{ statusLabel(lastRun(row.id).status) }}
          </el-tag>
          <span v-else style="color: #ccc; font-size: 12px;">未运行</span>
        </template>
      </el-table-column>
      <el-table-column prop="windowTitle" label="目标窗口" width="130" show-overflow-tooltip />
      <el-table-column prop="description" label="描述" min-width="180" show-overflow-tooltip />
      <el-table-column label="创建时间" width="160">
        <template #default="{ row }">
          {{ formatDate(row.createdAt) }}
        </template>
      </el-table-column>
      <el-table-column label="操作" width="230" fixed="right">
        <template #default="{ row }">
          <el-button size="small" type="success" @click="openRunDialog(row)">运行</el-button>
          <el-button size="small" @click="$router.push(`/scenarios/${row.id}/edit`)">编辑</el-button>
          <el-dropdown style="margin-left: 8px;" @command="(cmd: string) => handleMore(cmd, row)">
            <el-button size="small">
              更多 <el-icon style="margin-left: 2px;"><ArrowDown /></el-icon>
            </el-button>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item command="copy">📋 复制场景</el-dropdown-item>
                <el-dropdown-item command="history">📊 执行历史</el-dropdown-item>
                <el-dropdown-item command="delete" divided>
                  <span style="color: #f56c6c;">🗑 删除</span>
                </el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </template>
      </el-table-column>
    </el-table>
    </div>

    <!-- 运行参数对话框 -->
    <el-dialog
      v-model="runDialogVisible"
      width="480px"
      :show-close="true"
      align-center
    >
      <template #header>
        <div class="run-dialog-header">
          <span class="run-dialog-title">运行场景</span>
          <span class="run-dialog-name">{{ runScenario?.name }}</span>
        </div>
      </template>

      <div v-if="runScenario" class="run-dialog-body">

        <!-- 执行模式 -->
        <div class="section-label">执行模式</div>
        <div class="mode-group">
          <div
            v-for="(m, i) in modeOptions"
            :key="m.value"
            class="mode-card"
            :class="{
              'mode-card-active':    runMode === m.value,
              'mode-card-disabled':  m.value === 'structured' && !hasSteps
            }"
            @click="m.value === 'structured' && !hasSteps ? null : (runMode = m.value as any)"
          >
            <div class="mode-card-left">
              <span class="mode-idx">{{ String(i + 1).padStart(2, '0') }}</span>
              <div class="mode-card-text">
                <div class="mode-name">{{ m.name }}</div>
                <div class="mode-desc">{{ m.value === 'auto' ? (hasSteps ? '有录制步骤 → 结构化执行' : '无录制步骤 → AI 推理') : (m.value === 'structured' && !hasSteps ? '该场景无录制步骤，不可用' : m.desc) }}</div>
              </div>
            </div>
            <div class="mode-card-right">
              <el-tag :type="m.tagType" size="small" effect="plain" class="mode-tag">{{ m.tag }}</el-tag>
              <div class="mode-radio" :class="{ 'mode-radio-on': runMode === m.value }" />
            </div>
          </div>
        </div>

        <!-- 运行参数 -->
        <template v-if="cleanParams.length > 0">
          <div class="section-label" style="margin-top:20px;">运行参数</div>
          <div class="params-grid">
            <div v-for="param in cleanParams" :key="param.name" class="param-item">
              <label class="param-label">{{ param.label || param.name }}</label>
              <el-input
                v-model="runParams[param.name]"
                :placeholder="param.defaultValue || '请输入'"
                size="default"
              />
            </div>
          </div>
        </template>

      </div>

      <template #footer>
        <div class="run-dialog-footer">
          <el-button @click="runDialogVisible = false" size="large">取消</el-button>
          <el-button type="primary" @click="startRun" :loading="running" size="large">
            ▶ 开始运行
          </el-button>
        </div>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Search, ArrowDown } from '@element-plus/icons-vue'
import { scenarioApi, taskApi } from '../api'

const router = useRouter()
const scenarios = ref<any[]>([])
const loading = ref(false)
const searchText = ref('')
const filterType = ref('')
// scenarioId → 最近一次运行记录
const lastRunMap = ref<Record<string, any>>({})
const runDialogVisible = ref(false)
const runScenario = ref<any>(null)
const runParams    = ref<Record<string, string>>({})
const runMode      = ref<'auto' | 'structured' | 'ai'>('auto')
const running      = ref(false)

// 当前场景是否有录制步骤
const hasSteps = computed(() => {
  try {
    const steps = JSON.parse(runScenario.value?.stepsJson || '[]')
    return Array.isArray(steps) && steps.length > 0
  } catch { return false }
})

// 模式选项配置
const modeOptions = [
  { value: 'auto',       icon: '⚡', name: '自动',       desc: '',                       tag: '推荐',      tagType: 'primary' },
  { value: 'structured', icon: '🎯', name: '结构化执行', desc: '直接执行录制步骤，不依赖 AI', tag: '准确率 ~98%', tagType: 'success' },
  { value: 'ai',         icon: '🤖', name: 'AI 推理执行', desc: 'AI 读取界面自行决策',       tag: '准确率 ~70%', tagType: 'warning' }
]

const parsedParams = computed(() => {
  if (!runScenario.value) return []
  try { return JSON.parse(runScenario.value.parametersJson || '[]') }
  catch { return [] }
})

// 过滤掉日文/类名等无意义参数
const cleanParams = computed(() =>
  parsedParams.value.filter((p: any) => {
    const label = (p.label || p.name || '')
    // 过滤包含日文字符、命名空间、冒号的参数名
    return !/[぀-ヿ一-鿿]/.test(label.slice(0, 4)) &&
           !label.includes('BridgeVision') &&
           !label.includes(':') &&
           label.length < 50
  })
)

// 搜索 + 类型过滤
const filteredScenarios = computed(() =>
  scenarios.value.filter(s => {
    if (filterType.value && (s.type || 'wpf') !== filterType.value) return false
    if (searchText.value && !s.name?.toLowerCase().includes(searchText.value.toLowerCase())) return false
    return true
  })
)

function lastRun(scenarioId: string) {
  return lastRunMap.value[scenarioId]
}

function statusType(s: string) {
  const map: Record<string, any> = { running: 'warning', passed: 'success', failed: 'danger', cancelled: 'info' }
  return map[s] || 'info'
}

function statusLabel(s: string) {
  const map: Record<string, string> = { running: '运行中', passed: '通过', failed: '失败', cancelled: '已取消', pending: '等待中' }
  return map[s] || s
}

async function loadScenarios() {
  loading.value = true
  try {
    const res = await scenarioApi.list()
    scenarios.value = res.data
  } catch {
    ElMessage.error('加载场景失败')
  } finally {
    loading.value = false
  }
}

// 拉取最近 100 条历史，取每个场景的最新一次运行状态
async function loadLastRuns() {
  try {
    const res = await taskApi.history()
    const map: Record<string, any> = {}
    for (const run of res.data) {
      if (!map[run.scenarioId]) map[run.scenarioId] = run  // 接口已按时间倒序
    }
    lastRunMap.value = map
  } catch { }
}

async function handleMore(cmd: string, row: any) {
  if (cmd === 'copy') {
    try {
      const res = await scenarioApi.get(row.id)
      const s = res.data
      await scenarioApi.create({
        name: `${s.name}_副本`,
        type: s.type,
        suiteId: s.suiteId,
        windowTitle: s.windowTitle,
        description: s.description,
        stepsJson: s.stepsJson,
        parametersJson: s.parametersJson,
        assertionsJson: s.assertionsJson,
        maxSteps: s.maxSteps
      })
      ElMessage.success(`已复制为「${s.name}_副本」`)
      loadScenarios()
    } catch {
      ElMessage.error('复制失败')
    }
  } else if (cmd === 'history') {
    router.push(`/history?scenarioId=${row.id}`)
  } else if (cmd === 'delete') {
    deleteScenario(row)
  }
}

function stepCount(row: any) {
  try {
    const steps = JSON.parse(row.stepsJson || '[]')
    return Array.isArray(steps) ? steps.length : 0
  } catch { return 0 }
}

function formatDate(dateStr: string) {
  if (!dateStr) return ''
  return new Date(dateStr).toLocaleString('zh-CN')
}

function openRunDialog(scenario: any) {
  runScenario.value = scenario
  runParams.value   = {}
  runMode.value     = 'auto'
  // 填充默认值
  const params = JSON.parse(scenario.parametersJson || '[]')
  for (const p of params) {
    runParams.value[p.name] = p.defaultValue || ''
  }
  runDialogVisible.value = true
}

async function startRun() {
  if (!runScenario.value) return
  running.value = true
  try {
    const res = await taskApi.run(runScenario.value.id, runParams.value, runMode.value)
    runDialogVisible.value = false
    ElMessage.success('已开始运行')
    router.push(`/monitor/${res.data.runId}`)
  } catch (e: any) {
    ElMessage.error(e.response?.data || '启动失败')
  } finally {
    running.value = false
  }
}

async function deleteScenario(row: any) {
  try {
    await ElMessageBox.confirm(`确定删除场景 "${row.name}"？`, '确认删除', { type: 'warning' })
    await scenarioApi.delete(row.id)
    ElMessage.success('删除成功')
    loadScenarios()
  } catch {
    // 取消删除
  }
}

onMounted(() => {
  loadScenarios()
  loadLastRuns()
})
</script>

<style scoped>
/* 表格纸面：白底、方角、发丝边框 */
.data-card {
  background: var(--app-surface);
  border-radius: 2px;
  border: 1px solid var(--line);
  padding: 4px 16px;
}

/* ── 弹窗头部 ── */
.run-dialog-header { display:flex; flex-direction:column; gap:4px; }
.run-dialog-title  { font-family: var(--font-serif); font-size:22px; font-weight:600; color:var(--app-ink); }
.run-dialog-name   { font-family: var(--font-mono); font-size:12px; color:var(--app-text-sub); letter-spacing:.3px; }

/* ── 弹窗主体 ── */
.run-dialog-body { padding:0 2px; }
.section-label {
  font-family: var(--font-mono);
  font-size:11px; font-weight:400; color:var(--app-text-mut);
  text-transform:uppercase; letter-spacing:1.5px;
  margin-bottom:12px;
}

/* ── 模式卡片：方角、墨黑激活、左墨条 + mono 编号 ── */
.mode-group { display:flex; flex-direction:column; gap:10px; }
.mode-card {
  position:relative;
  display:flex; align-items:center; justify-content:space-between;
  padding:14px 16px; border:1px solid var(--line); border-radius:2px;
  cursor:pointer; transition:border-color .15s, background .15s; background:var(--app-surface);
  user-select:none;
}
.mode-card::before {
  content:''; position:absolute; left:0; top:0; bottom:0; width:2px;
  background:var(--app-ink); transform:scaleY(0); transform-origin:center;
  transition:transform .18s ease;
}
.mode-card:hover         { border-color:var(--app-ink); background:#faf9f5; }
.mode-card-active        { border-color:var(--app-ink); background:#faf9f5; }
.mode-card-active::before { transform:scaleY(1); }
.mode-card-disabled      { opacity:.5; cursor:not-allowed; }
.mode-card-disabled:hover{ border-color:var(--line); background:var(--app-surface); }

.mode-card-left  { display:flex; align-items:center; gap:14px; min-width:0; }
.mode-idx {
  font-family: var(--font-mono); font-size:13px; font-weight:700;
  width:30px; height:30px; flex-shrink:0;
  display:flex; align-items:center; justify-content:center;
  border:1px solid var(--line); color:var(--app-text-mut);
  border-radius:2px; transition:all .15s;
}
.mode-card-active .mode-idx { background:var(--app-ink); color:#fff; border-color:var(--app-ink); }
.mode-card-text  { min-width:0; }
.mode-name       { font-size:14px; font-weight:600; color:var(--app-ink); line-height:1.4; }
.mode-desc       { font-size:12px; color:var(--app-text-sub); margin-top:3px; line-height:1.4; }

.mode-card-right { display:flex; align-items:center; gap:12px; flex-shrink:0; }
.mode-tag        { font-family:var(--font-mono); }
.mode-radio {
  width:16px; height:16px; border-radius:50%;
  border:1.5px solid var(--line); transition:all .2s; flex-shrink:0;
}
.mode-radio-on { border-color:var(--app-ink); background:var(--app-ink); box-shadow:inset 0 0 0 3px #fff; }

/* ── 参数 ── */
.params-grid { display:flex; flex-direction:column; gap:12px; }
.param-item  { display:flex; align-items:center; gap:14px; }
.param-label {
  width:130px; flex-shrink:0; font-size:13px; color:var(--app-text-sub);
  text-align:right; overflow:hidden; text-overflow:ellipsis; white-space:nowrap;
}

/* ── 底部 ── */
.run-dialog-footer { display:flex; justify-content:flex-end; gap:10px; }
</style>
