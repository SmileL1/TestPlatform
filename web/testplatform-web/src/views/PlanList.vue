<template>
  <div class="page page--full">
    <div class="page-head">
      <div>
        <h2 class="page-title">测试计划</h2>
        <div class="page-subtitle">将多个场景组合成计划，一键批量运行并查看整体结果</div>
      </div>
      <el-button type="primary" @click="openCreateDialog">
        <el-icon><Plus /></el-icon> 新建计划
      </el-button>
    </div>

    <div class="plan-grid" v-loading="loading">
      <div v-for="plan in plans" :key="plan.id" class="plan-card" @click="goToPlan(plan)">
        <div class="card-accent"></div>
        <div class="card-body">
          <div class="plan-top">
            <span class="plan-name">{{ plan.name }}</span>
            <div class="plan-actions" @click.stop>
              <el-tooltip content="执行历史" placement="top">
                <el-button text size="small" @click.stop="$router.push(`/plans/${plan.id}/history`)">
                  <el-icon><List /></el-icon>
                </el-button>
              </el-tooltip>
              <el-tooltip content="编辑" placement="top">
                <el-button text size="small" @click.stop="openEditDialog(plan)"><el-icon><Edit /></el-icon></el-button>
              </el-tooltip>
              <el-tooltip content="删除" placement="top">
                <el-button text size="small" type="danger" @click.stop="deletePlan(plan)"><el-icon><Delete /></el-icon></el-button>
              </el-tooltip>
            </div>
          </div>
          <p class="plan-desc">{{ plan.description || '暂无描述' }}</p>
          <div class="plan-bottom">
            <div class="plan-meta">
              <span class="meta-badge">
                <el-icon style="vertical-align:-2px; margin-right:3px;"><Tickets /></el-icon>
                {{ plan.scenarioCount }} 个场景
              </span>
              <span class="plan-date">
                <el-icon style="vertical-align:-2px; margin-right:3px;"><Calendar /></el-icon>
                {{ fmtDate(plan.createdAt) }}
              </span>
            </div>
            <el-button type="primary" size="small" round @click.stop="goToPlan(plan)">
              进入计划
            </el-button>
          </div>
        </div>
      </div>
      <div v-if="!loading && plans.length === 0" style="grid-column:1/-1; display:flex; justify-content:center;">
        <el-empty description="暂无测试计划，点击「新建计划」开始" :image-size="80" />
      </div>
    </div>

    <el-dialog v-model="dlgVisible" :title="editId ? '编辑计划' : '新建计划'" width="400px" align-center>
      <el-form :model="form" label-position="top">
        <el-form-item label="计划名称" required>
          <el-input v-model="form.name" placeholder="例如：6月传票管理回归测试" autofocus />
        </el-form-item>
        <el-form-item label="描述">
          <el-input v-model="form.description" type="textarea" :rows="2" placeholder="可选" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dlgVisible = false">取消</el-button>
        <el-button type="primary" @click="saveDlg" :loading="saving">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Plus, Edit, Delete, List, Tickets, Calendar } from '@element-plus/icons-vue'
import { planApi } from '../api'

const router  = useRouter()
const plans   = ref<any[]>([])
const loading = ref(false)
const saving  = ref(false)
const dlgVisible = ref(false)
const editId     = ref<string|null>(null)
const form = ref({ name: '', description: '' })

async function load() {
  loading.value = true
  try { plans.value = (await planApi.list()).data }
  catch { ElMessage.error('加载失败') }
  finally { loading.value = false }
}

function goToPlan(p: any) { router.push(`/plans/${p.id}`) }

function openCreateDialog() {
  editId.value = null; form.value = { name: '', description: '' }; dlgVisible.value = true
}
function openEditDialog(p: any) {
  editId.value = p.id; form.value = { name: p.name, description: p.description||'' }; dlgVisible.value = true
}

async function saveDlg() {
  if (!form.value.name.trim()) { ElMessage.warning('请输入计划名称'); return }
  saving.value = true
  try {
    editId.value ? await planApi.update(editId.value, form.value) : await planApi.create(form.value)
    dlgVisible.value = false; ElMessage.success('保存成功'); load()
  } catch { ElMessage.error('保存失败') } finally { saving.value = false }
}

async function deletePlan(p: any) {
  try {
    await ElMessageBox.confirm(`确定删除计划「${p.name}」？`, '确认', { type: 'warning' })
    await planApi.delete(p.id); ElMessage.success('删除成功'); load()
  } catch { }
}

const fmtDate = (d: string) => d ? new Date(d).toLocaleDateString('zh-CN') : ''
onMounted(load)
</script>

<style scoped>
.plan-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 20px;
}

.plan-card {
  position: relative;
  background: var(--app-surface);
  border-radius: 2px;
  overflow: hidden;
  cursor: pointer;
  border: 1px solid var(--line);
  transition: border-color .2s, box-shadow .2s;
}
.plan-card:hover {
  border-color: var(--app-ink);
  box-shadow: 0 1px 0 var(--app-ink);
}

/* 顶部规则线：墨黑发丝，hover 才显形 */
.card-accent {
  height: 2px;
  background: var(--line-strong);
  transform: scaleX(0);
  transform-origin: left;
  transition: transform .25s ease;
}
.plan-card:hover .card-accent { transform: scaleX(1); }

.card-body {
  padding: 18px 18px 14px;
}

.plan-top {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 8px;
}

.plan-name {
  font-family: var(--font-serif);
  font-size: 18px;
  font-weight: 600;
  color: var(--app-ink);
  flex: 1;
  margin-right: 8px;
  line-height: 1.25;
  word-break: break-all;
}

.plan-actions {
  display: flex;
  gap: 0;
  flex-shrink: 0;
  opacity: 0;
  transition: opacity .18s;
}
.plan-card:hover .plan-actions { opacity: 1; }

.plan-desc {
  font-size: 12.5px;
  color: #909399;
  margin: 0 0 14px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  line-height: 1.5;
}

.plan-bottom {
  display: flex;
  justify-content: space-between;
  align-items: center;
  border-top: 1px solid #f2f3f5;
  padding-top: 12px;
  margin-top: 2px;
}

.plan-meta {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.meta-badge {
  font-family: var(--font-mono);
  font-size: 11.5px;
  color: var(--app-text-sub);
  font-weight: 500;
  letter-spacing: .3px;
}

.plan-date {
  font-size: 11.5px;
  color: #b0b8c1;
}
</style>
