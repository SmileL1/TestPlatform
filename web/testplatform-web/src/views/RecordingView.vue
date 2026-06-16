<template>
  <div style="display:flex; flex-direction:column; height:100%; padding:24px; gap:16px; overflow:hidden;">

    <!-- 顶部控制栏 -->
    <el-card shadow="never">
      <div style="display:flex; align-items:center; gap:16px; flex-wrap:wrap;">
        <div style="font-size:17px; font-weight:650; letter-spacing:.3px;">🎙 操作录制</div>

        <el-select v-model="windowTitle" filterable allow-create default-first-option clearable
                   placeholder="目标窗口标题" style="width:240px;" :disabled="recording">
          <el-option v-for="w in windows" :key="w.pid + '_' + w.title"
                     :label="w.title + (w.processName ? '  ·  ' + w.processName : '')" :value="w.title" />
        </el-select>
        <el-button :loading="winLoading" :disabled="recording" @click="loadWindows">刷新窗口</el-button>

        <el-button v-if="!recording" type="danger" @click="startRecording" :loading="starting">
          <el-icon><VideoPlay /></el-icon> 开始录制
        </el-button>
        <el-button v-else type="warning" @click="stopRecording">
          <el-icon><VideoPause /></el-icon> 停止录制
        </el-button>

        <el-tag v-if="recording" type="danger" effect="dark" style="animation: blink 1s infinite;">
          🔴 录制中...（支持捕获点击、输入、Enter/Tab 等按键）
        </el-tag>

        <div style="margin-left:auto; display:flex; gap:8px; align-items:center;">
          <el-dropdown @command="addManualStep" :disabled="recording">
            <el-button :disabled="recording">
              ＋ 插入步骤 <el-icon style="margin-left:4px;"><ArrowDown /></el-icon>
            </el-button>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item command="enter">⏎ 按键 Enter</el-dropdown-item>
                <el-dropdown-item command="tab">⇥ 按键 Tab</el-dropdown-item>
                <el-dropdown-item command="escape">⎋ 按键 Escape</el-dropdown-item>
                <el-dropdown-item command="wait" divided>⏱ 等待 1 秒</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
          <el-button @click="clearSteps" :disabled="steps.length === 0">清空</el-button>
          <el-button type="primary" @click="showSaveDialog = true" :disabled="steps.length === 0">
            💾 保存为场景
          </el-button>
        </div>
      </div>
    </el-card>

    <!-- 录制步骤列表 -->
    <el-card shadow="never" style="flex:1; display:flex; flex-direction:column; overflow:hidden;">
      <template #header>
        <span>录制步骤</span>
        <el-tag style="margin-left:8px;">共 {{ steps.length }} 步</el-tag>
        <span style="color:#999; font-size:12px; margin-left:12px;">
          在被测应用中操作，步骤自动出现在此处；输入值可直接修改，支持上下调整顺序
        </span>
      </template>

      <div style="flex:1; min-height:0; overflow-y:auto;">
        <el-empty v-if="steps.length === 0" description="点击「开始录制」后在应用中操作" :image-size="100" />

        <div v-for="(step, i) in steps" :key="i"
             style="display:flex; align-items:center; padding:8px 12px; border-bottom:1px solid #f0f0f0; gap:12px;">

          <!-- 序号 -->
          <span style="color:#999; font-size:12px; width:28px; flex-shrink:0;">#{{ i + 1 }}</span>

          <!-- 动作标签 -->
          <el-tag size="small" :type="actionTagType(step.action)" style="flex-shrink:0; width:78px; text-align:center;">
            {{ step.action }}
          </el-tag>

          <!-- 描述 -->
          <span style="font-size:13px; flex-shrink:0;">{{ stepLabel(step) }}</span>

          <!-- 可编辑的值 -->
          <el-input v-if="hasEditableValue(step)" v-model="step.value"
                    size="small" style="width:150px; flex-shrink:0;" />

          <span style="flex:1;"></span>

          <!-- Target -->
          <el-tooltip :content="`Target: ${step.target}`" placement="top">
            <span style="color:#aaa; font-size:11px; max-width:160px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap;">
              {{ step.target }}
            </span>
          </el-tooltip>

          <!-- 时间 -->
          <span style="color:#ccc; font-size:11px; flex-shrink:0;">{{ step.time }}</span>

          <!-- 排序 / 删除 -->
          <div style="display:flex; flex-shrink:0;">
            <el-button text size="small" :disabled="i === 0" @click="moveStep(i, -1)">
              <el-icon><Top /></el-icon>
            </el-button>
            <el-button text size="small" :disabled="i === steps.length - 1" @click="moveStep(i, 1)">
              <el-icon><Bottom /></el-icon>
            </el-button>
            <el-button text type="danger" size="small" @click="removeStep(i)">
              <el-icon><Delete /></el-icon>
            </el-button>
          </div>
        </div>
      </div>
    </el-card>

    <!-- 保存场景对话框 -->
    <el-dialog v-model="showSaveDialog" title="保存为测试场景" width="480px">
      <el-form label-width="90px">
        <el-form-item label="场景名称">
          <el-input v-model="saveName" placeholder="如：基本传票发行" />
        </el-form-item>
        <el-form-item label="目标窗口">
          <el-input v-model="windowTitle" />
        </el-form-item>
        <el-form-item label="步骤预览">
          <div style="background:#f5f7fa; padding:12px; border-radius:4px; font-size:12px; max-height:200px; overflow-y:auto; width:100%;">
            <div v-for="(step, i) in steps" :key="i" style="margin-bottom:4px; color:#606266;">
              {{ i + 1 }}. {{ stepLabel(step) }}{{ hasEditableValue(step) ? `「${step.value}」` : '' }}
            </div>
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showSaveDialog = false">取消</el-button>
        <el-button type="primary" @click="saveScenario(false)" :loading="saving">保存</el-button>
        <el-button type="success" @click="saveScenario(true)" :loading="saving">
          💾 保存并立即运行
        </el-button>
      </template>
    </el-dialog>

  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { VideoPlay, VideoPause, Delete, ArrowDown, Top, Bottom } from '@element-plus/icons-vue'
import * as signalR from '@microsoft/signalr'
import axios from 'axios'
import { systemApi, settingsApi } from '../api'

const api = axios.create({ baseURL: 'http://localhost:5000/api' })
const router = useRouter()

// 当前运行的窗口列表（目标窗口选择器用）
const windows    = ref<{ title: string; processName: string; pid: number }[]>([])
const winLoading = ref(false)
async function loadWindows() {
  winLoading.value = true
  try { windows.value = (await systemApi.windows()).data }
  catch { ElMessage.error('获取窗口列表失败（请确认目标应用已打开）') }
  finally { winLoading.value = false }
}

const recording      = ref(false)
const starting       = ref(false)
const saving         = ref(false)
const showSaveDialog = ref(false)
const windowTitle    = ref('SmartZaiko')
const saveName       = ref('')
const steps          = ref<any[]>([])

let connection: signalR.HubConnection | null = null

// ── SignalR 连接 ──────────────────────────────────────────
async function connectSignalR() {
  connection = new signalR.HubConnectionBuilder()
    .withUrl('http://localhost:5000/hubs/test')
    .withAutomaticReconnect()
    .build()

  connection.on('RecordedStep', (step: any) => {
    // 防重：同一 index 更新 value（防抖合并）
    const existing = steps.value.find(s => s.index === step.index)
    if (existing) Object.assign(existing, step)
    else steps.value.push(step)
  })

  await connection.start()
  await connection.invoke('JoinRecording')
}

// ── 开始录制 ──────────────────────────────────────────────
async function startRecording() {
  starting.value = true
  try {
    await api.post('/recording/start', { windowTitle: windowTitle.value })
    recording.value = true
    steps.value = []
    saveName.value = `录制场景_${new Date().toLocaleTimeString('zh-CN', { hour:'2-digit', minute:'2-digit' })}`
    ElMessage.success('录制已开始，请在被测应用中操作')
  } catch (e: any) {
    ElMessage.error(e.response?.data?.error || '启动录制失败')
  } finally {
    starting.value = false
  }
}

// ── 停止录制 ──────────────────────────────────────────────
async function stopRecording() {
  try {
    const res = await api.post('/recording/stop')
    steps.value = res.data.steps
    recording.value = false
    ElMessage.success(`录制停止，共录制 ${res.data.count} 步，可在列表中编辑后保存`)
  } catch {
    ElMessage.error('停止录制失败')
  }
}

// ── 步骤编辑：删除 / 排序 / 手动插入 / 清空 ─────────────────
function removeStep(i: number) {
  const s = steps.value[i]
  steps.value.splice(i, 1)
  // 录制中同时同步删除后端，避免防抖更新把它复活
  if (recording.value && s?.index >= 0) {
    api.delete(`/recording/steps/${s.index}`).catch(() => {})
  }
}

function moveStep(i: number, dir: number) {
  const j = i + dir
  if (j < 0 || j >= steps.value.length) return
  const arr = steps.value
  ;[arr[i], arr[j]] = [arr[j], arr[i]]
}

function addManualStep(cmd: string) {
  const now = new Date().toLocaleTimeString('zh-CN', { hour12: false })
  const base = { index: -1, target: '', targetName: '', controlType: '', x: 0, y: 0, time: now }
  if (cmd === 'wait') {
    steps.value.push({ ...base, action: 'wait', value: '1000' })
  } else {
    const key = cmd.charAt(0).toUpperCase() + cmd.slice(1) // enter → Enter
    steps.value.push({ ...base, action: 'press_key', target: key, targetName: key, value: key })
  }
}

async function clearSteps() {
  steps.value = []
  try { await api.post('/recording/clear') } catch { }
}

// ── 保存场景（可选保存后立即运行）──────────────────────────
async function saveScenario(runAfterSave: boolean) {
  saving.value = true
  try {
    const res = await api.post('/recording/save', {
      name: saveName.value,
      windowTitle: windowTitle.value,
      steps: steps.value
    })
    ElMessage.success(res.data.message)
    showSaveDialog.value = false

    if (runAfterSave) {
      const scenario = res.data.scenario
      // 用提取出的默认参数立即做一次结构化回放
      const inputParams: Record<string, string> = {}
      try {
        for (const p of JSON.parse(scenario.parametersJson || '[]')) {
          inputParams[p.name] = p.defaultValue || ''
        }
      } catch { }
      const runRes = await api.post('/tasks/run', {
        scenarioId: scenario.id, inputParams, mode: 'structured'
      })
      ElMessage.success('已开始回放验证')
      router.push(`/monitor/${runRes.data.runId}`)
    } else {
      router.push('/scenarios')
    }
  } catch (e: any) {
    ElMessage.error(e.response?.data?.error || '保存失败')
  } finally {
    saving.value = false
  }
}

// ── 工具方法 ──────────────────────────────────────────────
function actionTagType(action: string) {
  const map: Record<string, string> = {
    click: 'success', set_text: 'warning',
    press_key: 'info', select_item: '',
    click_cell: 'success', wait: 'info'
  }
  return map[action] ?? ''
}

function hasEditableValue(step: any) {
  return ['set_text', 'select_item', 'press_key', 'wait'].includes(step.action)
}

function stepLabel(step: any) {
  const name = step.targetName || step.target
  switch (step.action) {
    case 'set_text':    return `在「${name}」输入`
    case 'select_item': return `在「${name}」选择`
    case 'press_key':   return '按键'
    case 'wait':        return '等待(毫秒)'
    case 'click_cell':  return step.description || `点击表格第${step.value}行「${step.targetName}」列`
    default:            return step.description || `点击「${name}」`
  }
}

onMounted(async () => {
  await connectSignalR()
  loadWindows()
  // 用「设置 → 默认测试目标」预填目标窗口（缺省保持 SmartZaiko）
  try {
    const def = (await settingsApi.get()).data.values?.['Target:DefaultWindow']
    if (def) windowTitle.value = def
  } catch { }
  // 加载已有步骤（可能之前已开始录制）
  try {
    const res = await api.get('/recording/steps')
    steps.value = res.data.steps
    recording.value = res.data.isRecording
  } catch { }
})

onUnmounted(async () => {
  if (connection) {
    try { await connection.invoke('LeaveRecording') } catch { }
    await connection.stop()
  }
})
</script>

<style scoped>
@keyframes blink { 0%,100% { opacity:1; } 50% { opacity:0.4; } }
</style>
