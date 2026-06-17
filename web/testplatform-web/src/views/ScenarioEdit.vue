<template>
  <div style="padding: 24px; height: 100%; box-sizing: border-box; overflow-y: auto;">
    <div style="display: flex; align-items: center; margin-bottom: 24px;">
      <el-button @click="$router.back()" style="margin-right: 12px;">
        <el-icon><ArrowLeft /></el-icon> 返回
      </el-button>
      <h2 style="margin: 0;">{{ isEdit ? '编辑场景' : '新建场景' }}</h2>
    </div>

    <el-form :model="form" label-width="120px" style="max-width: 900px;" v-loading="loading">
      <!-- 基本信息 -->
      <el-card style="margin-bottom: 20px;">
        <template #header><b>基本信息</b></template>
        <el-form-item label="场景名称" required>
          <el-input v-model="form.name" placeholder="请输入场景名称" />
        </el-form-item>
        <el-form-item label="类型">
          <el-tag :type="form.type === 'web' ? 'success' : 'primary'" style="margin-right:8px;">
            {{ form.type === 'web' ? '🌐 Web 网页' : '🖥 WPF 桌面' }}
          </el-tag>
          <span style="color:#909399; font-size:12px;">（创建后不可修改）</span>
          <div v-if="form.type === 'web'" style="color:#e6a23c; font-size:12px; margin-top:6px;">
            ⓘ Web 场景走 AI 推理执行（浏览器自动化），暂不支持录制回放。
          </div>
        </el-form-item>
        <el-form-item :label="form.type === 'web' ? '起始 URL' : '目标窗口'">
          <el-input v-if="form.type === 'web'" v-model="form.windowTitle"
            placeholder="例如：http://localhost:3000" />
          <div v-else style="display:flex; gap:10px; width:100%;">
            <el-select v-model="form.windowTitle" filterable allow-create default-first-option clearable
                       placeholder="选择或输入窗口标题，如 SmartZaiko" style="flex:1;">
              <el-option v-for="w in windows" :key="w.pid + '_' + w.title"
                         :label="w.title + (w.processName ? '  ·  ' + w.processName : '')" :value="w.title" />
            </el-select>
            <el-button :loading="winLoading" @click="loadWindows">刷新窗口</el-button>
          </div>
        </el-form-item>
        <el-form-item label="最大步数">
          <el-input-number v-model="form.maxSteps" :min="1" :max="200" />
        </el-form-item>
      </el-card>

      <!-- 描述 -->
      <el-card style="margin-bottom: 20px;">
        <template #header>
          <div style="display:flex; justify-content:space-between; align-items:center;">
            <span><b>自然语言描述</b>
              <span style="color: #999; font-size: 12px; margin-left: 8px;" v-pre>支持 {{参数名}} 占位符</span>
            </span>
            <el-popover placement="left" :width="380" trigger="click">
              <template #reference>
                <el-button text size="small" type="info">🌐 浏览器测试写法</el-button>
              </template>
              <div style="font-size:13px; line-height:1.8;">
                <b>浏览器工具（网页自动化）</b>
                <el-divider style="margin:6px 0;" />
                <div style="font-family:monospace; font-size:12px; color:#333;">
                  <div>1. browser_scan() → 扫描页面元素</div>
                  <div>2. browser_fill("#username", "admin") → 填写</div>
                  <div>3. browser_select("#product", "苹果") → 下拉选择</div>
                  <div>4. browser_click("#agree") → 点击（复选框等）</div>
                  <div>5. browser_click_text("提交订单") → 按文字点击</div>
                  <div>6. assert_text("#result", "成功") → 断言</div>
                </div>
                <el-divider style="margin:6px 0;" />
                <div style="color:#909399; font-size:12px;">
                  起始 URL 在上方「起始 URL」填写，运行时会自动打开浏览器并导航，无需手动启动 Chrome。
                </div>
              </div>
            </el-popover>
          </div>
        </template>
        <el-form-item label="目标描述">
          <el-input
            v-model="form.description"
            type="textarea"
            :rows="6"
            placeholder="WPF示例：在車番输入框输入 {{车牌号}}，按Enter搜索，填写总重 {{総重}}
网页示例：browser_connect()，打开 {{url}}，点击登录按钮，填写用户名 {{username}}"
          />
        </el-form-item>
      </el-card>

      <!-- 录制步骤 -->
      <el-card style="margin-bottom: 20px;">
        <template #header>
          <div style="display: flex; justify-content: space-between; align-items: center;">
            <span>
              <b>录制步骤</b>
              <el-tag v-if="steps.length > 0" size="small" type="success" style="margin-left:8px;">
                {{ steps.length }} 步 · 默认结构化回放
              </el-tag>
              <span style="color:#999; font-size:12px; margin-left:8px;">
                输入值支持 <span v-pre>{{参数名}}</span> 占位符
              </span>
            </span>

            <!-- 非录制态：添加步骤 + 末尾追加录制 -->
            <div v-if="!recording" style="display:flex; gap:8px;">
              <el-dropdown @command="addStep">
                <el-button size="small">
                  ＋ 添加步骤 <el-icon style="margin-left:4px;"><ArrowDown /></el-icon>
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
              <el-button size="small" type="success" :disabled="!form.windowTitle" @click="appendRecord">
                <el-icon><VideoCamera /></el-icon> {{ form.type === 'web' ? '开始网页录制' : '末尾追加录制' }}
              </el-button>
            </div>

            <!-- 录制态：停止并应用 / 取消 -->
            <div v-else style="display:flex; gap:8px; align-items:center;">
              <el-tag type="danger" effect="dark"
                      style="animation: rec-blink 1s infinite; background:#f56565 !important; border-color:#f56565 !important; color:#fff !important; font-weight:600;">🔴 录制中</el-tag>
              <el-button size="small" type="primary" @click="stopAndApply" :loading="applying">停止并应用</el-button>
              <el-button size="small" @click="cancelRecord">取消</el-button>
            </div>
          </div>
        </template>

        <el-table v-if="steps.length > 0" :data="steps" border size="small">
          <el-table-column type="index" width="50" align="center" />
          <el-table-column label="动作" width="100">
            <template #default="{ row }">
              <el-tag size="small">{{ row.action }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="目标元素" min-width="160">
            <template #default="{ row }">
              <div>{{ row.targetName || row.target || '—' }}</div>
              <div style="color:#bbb; font-size:11px;">{{ row.target }}</div>
            </template>
          </el-table-column>
          <el-table-column label="值" min-width="150">
            <template #default="{ row }">
              <el-input v-if="stepHasValue(row)" v-model="row.value" size="small" />
              <span v-else style="color:#ccc;">—</span>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="170" align="center">
            <template #default="{ $index }">
              <el-button text size="small" :disabled="recording || $index === 0" @click="moveStep($index, -1)">
                <el-icon><Top /></el-icon>
              </el-button>
              <el-button text size="small" :disabled="recording || $index === steps.length - 1" @click="moveStep($index, 1)">
                <el-icon><Bottom /></el-icon>
              </el-button>
              <el-tooltip content="从此步重新录制（丢弃此步及之后，再续录新操作）" placement="top">
                <el-button text type="primary" size="small" :disabled="recording || !form.windowTitle"
                           @click="startRecordFrom($index)">
                  <el-icon><VideoCamera /></el-icon>
                </el-button>
              </el-tooltip>
              <el-button text type="danger" size="small" :disabled="recording" @click="steps.splice($index, 1)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
        <p v-else-if="!recording" style="color: #999; text-align: center; padding: 16px;">
          暂无录制步骤——可到「操作录制」页面录制后保存；无步骤时运行将使用 AI 推理执行
        </p>

        <!-- 录制中：实时显示新录的步骤 -->
        <div v-if="recording" class="live-rec">
          <div class="live-rec-head">
            🔴 正在录制新步骤（接在第 {{ recordFromIndex }} 步之后）—
            {{ form.type === 'web' ? '请在弹出的浏览器窗口中操作' : `请切换到被测应用「${form.windowTitle}」操作` }}
          </div>
          <div v-if="liveSteps.length === 0" class="live-rec-empty">
            {{ form.type === 'web' ? '等待操作…在弹出的浏览器里点击 / 输入即可实时出现' : '等待操作…在被测应用中点击 / 输入即可实时出现' }}
          </div>
          <div v-for="(s, i) in liveSteps" :key="i" class="live-rec-item">
            <span class="live-rec-num">{{ recordFromIndex + i + 1 }}</span>
            <el-tag size="small" type="warning">{{ s.action }}</el-tag>
            <span>{{ stepText(s) }}</span>
          </div>
        </div>
      </el-card>

      <!-- 参数定义 -->
      <el-card style="margin-bottom: 20px;">
        <template #header>
          <div style="display: flex; justify-content: space-between; align-items: center;">
            <b>参数定义</b>
            <el-button size="small" type="primary" @click="addParam">
              <el-icon><Plus /></el-icon> 添加参数
            </el-button>
          </div>
        </template>
        <el-table :data="parameters" border>
          <el-table-column label="参数名（占位符）" width="200">
            <template #default="{ row }">
              <el-input v-model="row.name" placeholder="例如：branch" size="small" />
            </template>
          </el-table-column>
          <el-table-column label="显示标签">
            <template #default="{ row }">
              <el-input v-model="row.label" placeholder="例如：分支名称" size="small" />
            </template>
          </el-table-column>
          <el-table-column label="默认值">
            <template #default="{ row }">
              <el-input v-model="row.defaultValue" placeholder="可选" size="small" />
            </template>
          </el-table-column>
          <el-table-column label="操作" width="80" align="center">
            <template #default="{ $index }">
              <el-button size="small" type="danger" @click="parameters.splice($index, 1)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
        <p v-if="parameters.length === 0" style="color: #999; text-align: center; padding: 16px;">暂无参数</p>
      </el-card>

      <!-- 验证条件（WPF 结构化断言；Web 场景走 AI 截图验证，不显示此卡片）-->
      <el-card v-if="form.type !== 'web'" style="margin-bottom: 20px;">
        <template #header>
          <div style="display: flex; justify-content: space-between; align-items: center;">
            <span>
              <b>验证条件</b>
              <span style="color:#909399; font-size:12px; margin-left:8px;">
                回放后读取控件值判定成败；设置后以验证结果为准，不再因个别步骤失败而判失败
              </span>
            </span>
            <el-button size="small" type="primary" @click="addAssertion">
              <el-icon><Plus /></el-icon> 添加条件
            </el-button>
          </div>
        </template>
        <el-table v-if="assertions.length > 0" :data="assertions" border size="small">
          <el-table-column label="验证方式" width="150">
            <template #default="{ row }">
              <el-select v-model="row.op" size="small" style="width:100%;" @change="onOpChange(row)">
                <el-option-group label="读控件值">
                  <el-option label="控件值等于" value="equals" />
                  <el-option label="控件值包含" value="contains" />
                  <el-option label="控件值非空" value="notEmpty" />
                </el-option-group>
                <el-option-group label="界面状态">
                  <el-option label="控件存在(已跳转)" value="exists" />
                  <el-option label="控件不存在" value="notExists" />
                  <el-option label="界面出现文本" value="textVisible" />
                  <el-option label="界面无此文本" value="textNotVisible" />
                </el-option-group>
                <el-option-group label="弹窗检查">
                  <el-option label="无任何弹窗" value="noDialog" />
                  <el-option label="无此错误提示" value="dialogNotContains" />
                  <el-option label="出现此提示" value="dialogContains" />
                </el-option-group>
              </el-select>
            </template>
          </el-table-column>
          <el-table-column label="控件 AutomationId" min-width="170">
            <template #default="{ row }">
              <el-select v-if="needsElementId(row.op)" v-model="row.elementId"
                         filterable allow-create default-first-option
                         placeholder="选择或输入控件 id" size="small" style="width:100%;">
                <el-option v-for="id in elementIdOptions" :key="id" :label="id" :value="id" />
              </el-select>
              <span v-else style="color:#ccc;">—</span>
            </template>
          </el-table-column>
          <el-table-column label="期望值 / 文本" min-width="150">
            <template #default="{ row }">
              <el-input v-if="needsExpected(row.op)" v-model="row.expected"
                        :placeholder="expectedPlaceholder(row.op)" size="small" />
              <span v-else style="color:#ccc;">—</span>
            </template>
          </el-table-column>
          <el-table-column label="备注" min-width="110">
            <template #default="{ row }">
              <el-input v-model="row.label" placeholder="选填" size="small" />
            </template>
          </el-table-column>
          <el-table-column label="操作" width="70" align="center">
            <template #default="{ $index }">
              <el-button size="small" type="danger" @click="assertions.splice($index, 1)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
        <p v-else style="color: #999; text-align: center; padding: 16px;">
          暂无验证条件——不设置时按“所有步骤无失败”判定通过。常用：发行后「控件存在」某结果页控件、「无此错误提示」失败
        </p>

        <!-- AI 截图验证 -->
        <el-divider style="margin:16px 0 12px;" />
        <div style="display:flex; align-items:center; gap:10px;">
          <el-switch v-model="form.aiVerifyEnabled" />
          <span style="font-weight:600;">🤖 启用 AI 截图验证</span>
          <span style="color:#909399; font-size:12px;">
            回放结束后截取界面图，连同测试目标交多模态模型判断（与上方条件同时通过才算通过）
          </span>
        </div>
        <el-input
          v-if="form.aiVerifyEnabled"
          v-model="form.aiVerifyPrompt"
          type="textarea" :rows="2"
          style="margin-top:10px;"
          placeholder="额外验证要点（可选）。如：确认顶部出现“発行済”，且没有红色错误提示。留空则只用上方场景描述判断。"
        />
        <p v-if="form.aiVerifyEnabled" style="color:#e6a23c; font-size:12px; margin-top:6px;">
          ⚠ 需在后端 appsettings.json 的 AiVision 配置支持图像的模型（如 gpt-4o / qwen-vl-max）；未配置则该验证自动跳过、不影响判定。
        </p>
      </el-card>

      <!-- 操作按钮 -->
      <div style="display: flex; gap: 12px; justify-content: flex-end;">
        <el-button @click="$router.back()">取消</el-button>
        <el-button type="primary" @click="save" :loading="saving">保存</el-button>
      </div>
    </el-form>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, ArrowLeft, ArrowDown, Top, Bottom, VideoCamera } from '@element-plus/icons-vue'
import * as signalR from '@microsoft/signalr'
import { scenarioApi, recordingApi, systemApi, settingsApi } from '../api'

const route = useRoute()
const router = useRouter()
const loading = ref(false)
const saving = ref(false)

const isEdit = computed(() => !!route.params.id)

// 从套件详情页跳转时带过来的参数
const suiteId    = route.query.suiteId as string | undefined
const sceneType  = (route.query.type as string) || 'wpf'

const form = reactive({
  name: '',
  windowTitle: sceneType === 'web' ? '' : 'SmartZaiko',
  description: '',
  maxSteps: 60,
  type: sceneType,
  suiteId: suiteId || null,
  aiVerifyEnabled: false,
  aiVerifyPrompt: ''
})

const parameters = ref<any[]>([])
const assertions = ref<any[]>([])
const steps      = ref<any[]>([])

// 当前运行的窗口列表（目标窗口选择器用）
const windows    = ref<{ title: string; processName: string; pid: number }[]>([])
const winLoading = ref(false)
async function loadWindows() {
  winLoading.value = true
  try { windows.value = (await systemApi.windows()).data }
  catch { ElMessage.error('获取窗口列表失败（请确认后端已启动、目标应用已打开）') }
  finally { winLoading.value = false }
}

function addParam() {
  parameters.value.push({ name: '', label: '', defaultValue: '' })
}

function addAssertion() {
  assertions.value.push({ elementId: '', op: 'equals', expected: '', label: '' })
}

// 哪些验证方式需要 控件id / 期望文本
function needsElementId(op: string) {
  return ['equals', 'contains', 'notEmpty', 'exists', 'notExists'].includes(op)
}
function needsExpected(op: string) {
  return ['equals', 'contains', 'textVisible', 'textNotVisible', 'dialogContains', 'dialogNotContains'].includes(op)
}
function expectedPlaceholder(op: string) {
  if (op === 'textVisible' || op === 'textNotVisible') return '界面文本，如：発行済'
  if (op === 'dialogContains' || op === 'dialogNotContains') return '弹窗文本，如：失敗'
  return '支持 {{参数}}'
}
// 切换方式后清掉不适用的字段，避免残留脏值
function onOpChange(row: any) {
  if (!needsElementId(row.op)) row.elementId = ''
  if (!needsExpected(row.op)) row.expected = ''
}

// 验证条件控件 id 下拉建议：录制步骤里出现过的、可定位的 AutomationId
const elementIdOptions = computed(() => {
  const ids = new Set<string>()
  for (const s of steps.value) {
    if (s.target && !s.target.startsWith('pos(') && !['press_key', 'wait'].includes(s.action))
      ids.add(s.target)
  }
  return [...ids]
})

// ── 录制步骤编辑 ──────────────────────────────────────────
function stepHasValue(row: any) {
  return ['set_text', 'select_item', 'press_key', 'wait', 'click_cell'].includes(row.action)
}

function moveStep(i: number, dir: number) {
  const j = i + dir
  if (j < 0 || j >= steps.value.length) return
  const arr = steps.value
  ;[arr[i], arr[j]] = [arr[j], arr[i]]
}

function addStep(cmd: string) {
  const base = { target: '', targetName: '', controlType: '', x: 0, y: 0 }
  if (cmd === 'wait') {
    steps.value.push({ ...base, action: 'wait', value: '1000' })
  } else {
    const key = cmd.charAt(0).toUpperCase() + cmd.slice(1)
    steps.value.push({ ...base, action: 'press_key', target: key, targetName: key, value: key })
  }
}

// 步骤可读文本（录制中实时列表用）
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

// ── 局部重新录制 ──────────────────────────────────────────
const recording      = ref(false)
const applying       = ref(false)
const recordFromIndex = ref(0)        // 保留 0..recordFromIndex-1，从这里续录
const liveSteps      = ref<any[]>([])
let   backupSteps: any[] = []         // 录制前的完整步骤，取消时恢复
let   recConn: signalR.HubConnection | null = null

// 从第 index 步重新录制（丢弃该步及之后）
async function startRecordFrom(index: number) {
  if (index < steps.value.length) {
    try {
      await ElMessageBox.confirm(
        `将丢弃第 ${index + 1} 步及之后的 ${steps.value.length - index} 个步骤，并从这里录制新操作。确定继续？`,
        '从此步重新录制', { type: 'warning' })
    } catch { return }
  }
  await beginRecording(index)
}

// 末尾追加录制（保留全部步骤）
async function appendRecord() {
  await beginRecording(steps.value.length)
}

async function beginRecording(fromIndex: number) {
  try {
    await recordingApi.start(form.windowTitle, form.type)
  } catch (e: any) {
    ElMessage.error(e.response?.data?.error ||
      (form.type === 'web' ? '启动浏览器录制失败，请确认起始 URL 正确' : '启动录制失败，请确认目标窗口已打开'))
    return
  }
  backupSteps        = [...steps.value]
  recordFromIndex.value = fromIndex
  steps.value        = steps.value.slice(0, fromIndex)   // 截断到保留部分
  liveSteps.value    = []
  recording.value    = true
  await connectRecordingSignalR()
  ElMessage.success('录制已开始，请切换到被测应用操作')
}

async function connectRecordingSignalR() {
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

async function teardownRecording() {
  if (recConn) {
    try { await recConn.invoke('LeaveRecording') } catch { }
    try { await recConn.stop() } catch { }
    recConn = null
  }
  recording.value = false
  liveSteps.value = []
}

// 停止并把新录的步骤接到保留部分后面
async function stopAndApply() {
  applying.value = true
  try {
    const res = await recordingApi.stop()        // 后端返回去重后的步骤
    const recorded = res.data.steps || []
    steps.value = [...steps.value, ...recorded]
    await teardownRecording()
    ElMessage.success(`已应用：续录 ${recorded.length} 步，当前共 ${steps.value.length} 步`)
  } catch {
    ElMessage.error('停止录制失败')
  } finally {
    applying.value = false
  }
}

// 取消录制，恢复原步骤
async function cancelRecord() {
  try { await recordingApi.stop() } catch { }   // 停掉后端钩子，丢弃结果
  steps.value = backupSteps
  await teardownRecording()
  ElMessage.info('已取消录制，步骤未改动')
}

async function loadScenario() {
  if (!isEdit.value) return
  loading.value = true
  try {
    const res = await scenarioApi.get(route.params.id as string)
    const s = res.data
    form.name = s.name
    form.windowTitle = s.windowTitle
    form.description = s.description
    form.maxSteps = s.maxSteps
    // 编辑时保留原有的类型和套件归属，避免保存时被覆盖
    form.type = s.type || 'wpf'
    form.suiteId = s.suiteId || null
    form.aiVerifyEnabled = !!s.aiVerifyEnabled
    form.aiVerifyPrompt = s.aiVerifyPrompt || ''
    parameters.value = JSON.parse(s.parametersJson || '[]')
    assertions.value = JSON.parse(s.assertionsJson || '[]')
    steps.value = JSON.parse(s.stepsJson || '[]')
  } catch {
    ElMessage.error('加载场景失败')
  } finally {
    loading.value = false
  }
}

async function save() {
  if (!form.name.trim()) {
    ElMessage.warning('请输入场景名称')
    return
  }
  saving.value = true
  try {
    const payload = {
      ...form,
      parametersJson: JSON.stringify(parameters.value),
      assertionsJson: JSON.stringify(assertions.value),
      stepsJson: JSON.stringify(steps.value)
    }
    if (isEdit.value) {
      await scenarioApi.update(route.params.id as string, payload)
      ElMessage.success('更新成功')
    } else {
      await scenarioApi.create(payload)
      ElMessage.success('创建成功')
    }
    router.push('/scenarios')
  } catch {
    ElMessage.error('保存失败')
  } finally {
    saving.value = false
  }
}

onMounted(async () => {
  await loadScenario()
  loadWindows()
  // 新建 WPF 场景：用「设置 → 默认测试目标」预填，缺省回退 SmartZaiko
  if (!isEdit.value && form.type !== 'web') {
    try {
      const def = (await settingsApi.get()).data.values?.['Target:DefaultWindow']
      if (def) form.windowTitle = def
    } catch { /* 设置未配置则保持默认 */ }
  }
})

// 离开页面时若仍在录制，停止后端录制并断开连接，避免钩子悬挂
onUnmounted(() => {
  if (recording.value) { recordingApi.stop().catch(() => {}) }
  teardownRecording()
})
</script>

<style scoped>
@keyframes rec-blink { 0%, 100% { opacity: 1; } 50% { opacity: .45; } }

.live-rec {
  margin-top: 12px;
  border: 1px dashed #c99089;
  border-radius: 2px;
  background: #faf6f4;
  padding: 12px 14px;
}
.live-rec-head {
  font-size: 13px;
  font-weight: 600;
  color: #e8543f;
  margin-bottom: 8px;
}
.live-rec-empty {
  font-size: 12px;
  color: #c0808a;
  padding: 6px 0;
}
.live-rec-item {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  color: #5a4145;
  padding: 5px 0;
  border-top: 1px solid #fbe3e3;
}
.live-rec-num {
  width: 22px; height: 22px;
  flex-shrink: 0;
  border-radius: 50%;
  background: #f56c6c;
  color: #fff;
  font-size: 11px;
  display: flex; align-items: center; justify-content: center;
}
</style>
