<template>
  <div style="padding:24px; height:100%; box-sizing:border-box; overflow-y:auto;">
    <!-- 头部 -->
    <div style="display:flex; align-items:center; gap:12px; margin-bottom:20px;">
      <el-button text @click="$router.push('/plans')"><el-icon><ArrowLeft /></el-icon> 计划列表</el-button>
      <div style="flex:1;">
        <h2 style="margin:0;">{{ plan?.name }}</h2>
        <p v-if="plan?.description" style="margin:2px 0 0; color:#909399; font-size:13px;">{{ plan.description }}</p>
      </div>
      <el-button @click="showAddDlg = true"><el-icon><Plus /></el-icon> 添加场景</el-button>
      <el-button type="primary" :loading="running" @click="showRunDlg = true" :disabled="!scenarios.length">
        <el-icon><VideoPlay /></el-icon> 运行计划
      </el-button>
    </div>

    <el-card>
      <template #header>
        <span style="font-weight:600;">计划场景</span>
        <el-tag style="margin-left:8px;" size="small">{{ scenarios.length }} 个</el-tag>
      </template>
      <el-empty v-if="!scenarios.length" description="还没有场景，点击「添加场景」" :image-size="60" />
      <div v-for="(s, i) in scenarios" :key="s.id" class="scenario-row">
        <span class="seq">{{ i + 1 }}</span>
        <el-tag :type="s.type === 'web' ? 'success' : 'primary'" size="small">{{ s.type === 'web' ? '🌐' : '🖥' }}</el-tag>
        <span class="sc-name">{{ s.name }}</span>
        <span class="sc-window">{{ s.windowTitle }}</span>
        <el-button
          v-if="s.type !== 'web'"
          text size="small" type="primary"
          @click="openRerecord(s)"
        >
          <el-icon><VideoCamera /></el-icon> 重新录制
        </el-button>
        <el-button text size="small" type="danger" @click="removeScenario(s)"><el-icon><Close /></el-icon></el-button>
      </div>
    </el-card>

    <!-- 重新录制整个场景 -->
    <el-dialog v-model="recDlg" :title="`重新录制：${recTarget?.name || ''}`" width="560px"
               :close-on-click-modal="false" @closed="onRecDlgClosed">
      <div style="margin-bottom:12px; color:#606266; font-size:13px;">
        目标窗口：<b>{{ recTarget?.windowTitle }}</b>
        <span style="color:#909399;">— 录制将覆盖该场景的全部步骤、描述与参数建议</span>
      </div>

      <div style="display:flex; gap:10px; align-items:center; margin-bottom:12px;">
        <el-button v-if="!recording" type="danger" @click="startRec" :loading="recStarting">
          <el-icon><VideoPlay /></el-icon> 开始录制
        </el-button>
        <template v-else>
          <el-tag type="danger" effect="dark" style="animation: rec-blink 1s infinite;">🔴 录制中</el-tag>
          <el-button type="primary" @click="stopAndSave" :loading="recSaving">停止并保存</el-button>
          <el-button @click="cancelRec">取消</el-button>
        </template>
        <span style="color:#909399; font-size:12px;">
          {{ recording ? '请切换到被测应用操作' : '点击开始后在被测应用中走一遍新流程' }}
        </span>
      </div>

      <div class="live-box">
        <el-empty v-if="liveSteps.length === 0" :description="recording ? '等待操作…' : '尚未录制'" :image-size="50" />
        <div v-for="(s, i) in liveSteps" :key="i" class="live-item">
          <span class="live-num">{{ i + 1 }}</span>
          <el-tag size="small" type="warning">{{ s.action }}</el-tag>
          <span class="live-text">{{ stepText(s) }}</span>
        </div>
      </div>

      <template #footer>
        <el-button @click="recDlg = false" :disabled="recording">关闭</el-button>
      </template>
    </el-dialog>

    <!-- 添加场景弹窗 -->
    <el-dialog v-model="showAddDlg" title="添加场景" width="600px">
      <el-input v-model="searchKey" placeholder="搜索场景名称..." clearable style="margin-bottom:12px;" />
      <el-table :data="filteredAllScenarios" max-height="360" @selection-change="selectedToAdd = $event" border size="small">
        <el-table-column type="selection" width="45" />
        <el-table-column label="类型" width="70" align="center">
          <template #default="{row}">
            <el-tag :type="row.type === 'web' ? 'success' : 'primary'" size="small">{{ row.type }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="name" label="场景名称" min-width="160" show-overflow-tooltip />
        <el-table-column prop="windowTitle" label="目标" width="120" show-overflow-tooltip />
      </el-table>
      <template #footer>
        <el-button @click="showAddDlg = false">取消</el-button>
        <el-button type="primary" @click="confirmAddScenarios" :loading="adding">
          添加已选 ({{ selectedToAdd.length }})
        </el-button>
      </template>
    </el-dialog>

    <!-- 运行计划弹窗 -->
    <el-dialog v-model="showRunDlg" title="运行测试计划" width="420px" align-center>
      <div style="margin-bottom:16px; color:#606266; font-size:13px;">
        将顺序执行计划中的 <b>{{ scenarios.length }}</b> 个场景
      </div>
      <el-form label-position="top">
        <el-form-item label="执行模式">
          <el-radio-group v-model="runMode">
            <el-radio value="auto">自动</el-radio>
            <el-radio value="structured">结构化（~98%）</el-radio>
            <el-radio value="ai">AI 推理（~70%）</el-radio>
          </el-radio-group>
        </el-form-item>
      </el-form>
      <el-alert type="info" :closable="false" show-icon style="margin-top:8px;">
        场景将依次顺序运行，单个场景失败不影响后续场景执行
      </el-alert>
      <template #footer>
        <el-button @click="showRunDlg = false">取消</el-button>
        <el-button type="primary" @click="startRun" :loading="running">▶ 开始运行</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowLeft, Plus, Close, VideoPlay, VideoCamera } from '@element-plus/icons-vue'
import * as signalR from '@microsoft/signalr'
import { planApi, scenarioApi, recordingApi } from '../api'

const route   = useRoute()
const router  = useRouter()
const planId  = route.params.id as string

const plan      = ref<any>(null)
const scenarios = ref<any[]>([])

const showAddDlg = ref(false)
const showRunDlg = ref(false)
const adding     = ref(false)
const running    = ref(false)
const runMode    = ref('auto')
const searchKey  = ref('')
const selectedToAdd = ref<any[]>([])
const allScenarios  = ref<any[]>([])

const filteredAllScenarios = computed(() => {
  const existIds = new Set(scenarios.value.map((s: any) => s.id))
  return allScenarios.value
    .filter(s => !existIds.has(s.id))
    .filter(s => !searchKey.value || s.name.includes(searchKey.value))
})

async function load() {
  const [planRes, scRes, allScRes] = await Promise.all([
    planApi.get(planId), planApi.getScenarios(planId), scenarioApi.list()
  ])
  plan.value         = planRes.data
  scenarios.value    = scRes.data
  allScenarios.value = allScRes.data
}

async function removeScenario(s: any) {
  try {
    await ElMessageBox.confirm(`从计划中移除「${s.name}」？`, '确认', { type: 'warning' })
    await planApi.removeScenario(planId, s.id)
    ElMessage.success('已移除'); load()
  } catch { }
}

async function confirmAddScenarios() {
  if (!selectedToAdd.value.length) { ElMessage.warning('请选择场景'); return }
  adding.value = true
  try {
    await planApi.addScenarios(planId, selectedToAdd.value.map(s => s.id))
    showAddDlg.value = false; ElMessage.success('添加成功'); load()
  } catch { ElMessage.error('添加失败') } finally { adding.value = false }
}

async function startRun() {
  running.value = true
  try {
    const res = await planApi.run(planId, { mode: runMode.value })
    showRunDlg.value = false
    // 跳转到执行详情页
    router.push(`/plans/${planId}/run/${res.data.planRunId}`)
  } catch (e: any) {
    ElMessage.error(e.response?.data?.error || '启动失败')
  } finally { running.value = false }
}


// ── 重新录制整个场景 ──────────────────────────────────────
const recDlg      = ref(false)
const recTarget   = ref<any>(null)
const recording   = ref(false)
const recStarting = ref(false)
const recSaving   = ref(false)
const liveSteps   = ref<any[]>([])
let   recConn: signalR.HubConnection | null = null

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

function openRerecord(s: any) {
  recTarget.value = s
  liveSteps.value = []
  recording.value = false
  recDlg.value    = true
}

async function startRec() {
  recStarting.value = true
  try {
    await recordingApi.start(recTarget.value.windowTitle)
    liveSteps.value = []
    recording.value = true
    await connectRec()
    ElMessage.success('录制已开始，请切换到被测应用操作')
  } catch (e: any) {
    ElMessage.error(e.response?.data?.error || '启动录制失败，请确认目标窗口已打开')
  } finally {
    recStarting.value = false
  }
}

async function connectRec() {
  recConn = new signalR.HubConnectionBuilder()
    .withUrl('http://localhost:5000/hubs/test')
    .withAutomaticReconnect()
    .build()
  recConn.on('RecordedStep', (step: any) => {
    const existing = liveSteps.value.find(s => s.index === step.index)
    if (existing) Object.assign(existing, step)
    else liveSteps.value.push(step)
  })
  await recConn.start()
  await recConn.invoke('JoinRecording')
}

async function teardownRec() {
  if (recConn) {
    try { await recConn.invoke('LeaveRecording') } catch { }
    try { await recConn.stop() } catch { }
    recConn = null
  }
  recording.value = false
}

async function stopAndSave() {
  recSaving.value = true
  try {
    const res = await recordingApi.stop()
    const steps = res.data.steps || []
    if (steps.length === 0) {
      ElMessage.warning('没有录制到任何步骤')
      await teardownRec()
      return
    }
    const saveRes = await recordingApi.saveTo(recTarget.value.id, steps)
    await teardownRec()
    recDlg.value = false
    ElMessage.success(saveRes.data.message || '已保存')
    load()   // 刷新场景列表（步骤数等）
  } catch {
    ElMessage.error('保存失败')
  } finally {
    recSaving.value = false
  }
}

async function cancelRec() {
  try { await recordingApi.stop() } catch { }
  await teardownRec()
  liveSteps.value = []
  ElMessage.info('已取消录制，场景未改动')
}

function onRecDlgClosed() {
  // 对话框关闭时若仍在录制，停止后端录制避免钩子悬挂
  if (recording.value) { recordingApi.stop().catch(() => {}); teardownRec() }
  liveSteps.value = []
  recTarget.value = null
}

onMounted(load)
onUnmounted(() => { if (recording.value) { recordingApi.stop().catch(() => {}); teardownRec() } })
</script>

<style scoped>
.scenario-row { display:flex; align-items:center; gap:8px; padding:8px 0; border-bottom:1px solid #f0f0f0; }
.scenario-row:last-child { border-bottom:none; }
.seq      { width:20px; color:#c0c4cc; font-size:12px; text-align:center; flex-shrink:0; }
.sc-name  { flex:1; font-size:13px; color:var(--app-ink); overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
.sc-window{ font-size:12px; color:#909399; width:100px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; flex-shrink:0; }
.run-row  { padding:10px 0; border-bottom:1px solid #f0f0f0; cursor:pointer; }
.run-row:hover { background:#f5f7fa; }
.run-row:last-child { border-bottom:none; }
.run-row-top { display:flex; justify-content:space-between; margin-bottom:6px; }
.run-date { font-size:12px; color:#c0c4cc; }
.run-stats { display:flex; align-items:center; gap:4px; font-size:12px; }
.stat-pass { color:#67c23a; font-weight:600; }
.stat-fail { color:#f56c6c; font-weight:600; }
.stat-total{ color:#909399; }

/* 重新录制对话框 */
@keyframes rec-blink { 0%,100% { opacity:1; } 50% { opacity:.45; } }
.live-box {
  border: 1px dashed #c99089;
  border-radius: 2px;
  background: #faf6f4;
  padding: 10px 12px;
  max-height: 280px;
  overflow-y: auto;
}
.live-item {
  display: flex; align-items: center; gap: 8px;
  font-size: 13px; color: #5a4145;
  padding: 5px 0;
  border-top: 1px solid #fbe3e3;
}
.live-item:first-child { border-top: none; }
.live-num {
  width: 22px; height: 22px; flex-shrink: 0;
  border-radius: 50%; background: #f56c6c; color: #fff;
  font-size: 11px; display: flex; align-items: center; justify-content: center;
}
.live-text { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
</style>
