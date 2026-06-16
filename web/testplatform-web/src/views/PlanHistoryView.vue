<template>
  <div style="padding:24px; height:100%; box-sizing:border-box; overflow-y:auto;">
    <div style="display:flex; align-items:center; gap:12px; margin-bottom:24px;">
      <el-button text @click="$router.push(`/plans/${planId}`)">
        <el-icon><ArrowLeft /></el-icon> 返回计划
      </el-button>
      <h2 style="margin:0;">{{ planName }} · 运行历史</h2>
    </div>

    <el-empty v-if="!runs.length && !loading" description="暂无运行记录" />

    <div class="run-list" v-loading="loading">
      <div v-for="run in runs" :key="run.id" class="run-card" @click="goToRun(run)">
        <div class="run-card-left">
          <el-tag :type="runTagType(run.status)" size="small" effect="plain">{{ runLabel(run) }}</el-tag>
          <div class="run-time">{{ fmtTime(run.startedAt) }}</div>
        </div>
        <div class="run-progress">
          <div class="run-counts">
            <span class="cnt-pass">✓ {{ run.passedCount }}</span>
            <span class="cnt-fail">✗ {{ run.failedCount }}</span>
            <span class="cnt-total">/ {{ run.totalCount }}</span>
          </div>
          <el-progress
            :percentage="run.totalCount ? Math.round((run.passedCount / run.totalCount) * 100) : 0"
            :stroke-width="8"
            :status="run.status === 'completed' ? (run.failedCount ? 'exception' : 'success') : ''"
            style="flex:1;"
          />
        </div>
        <el-icon style="color:#c0c4cc;"><ArrowRight /></el-icon>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ArrowLeft, ArrowRight } from '@element-plus/icons-vue'
import { planApi } from '../api'

const route   = useRoute()
const router  = useRouter()
const planId  = route.params.id as string

const planName = ref('')
const runs     = ref<any[]>([])
const loading  = ref(false)

async function load() {
  loading.value = true
  try {
    const [planRes, runsRes] = await Promise.all([
      planApi.get(planId), planApi.getRuns(planId)
    ])
    planName.value = planRes.data.name
    runs.value     = runsRes.data
  } finally { loading.value = false }
}

function goToRun(run: any) {
  router.push(`/plans/${planId}/run/${run.id}`)
}

const runTagType = (s: string) => ({ running:'warning', completed:'success' }[s] || 'info')
const runLabel   = (run: any) => {
  if (run.status === 'running') return '运行中'
  if (run.status === 'completed') return run.failedCount ? `${run.failedCount} 个失败` : '全部通过'
  return run.status
}
const fmtTime = (d: string) => d ? new Date(d).toLocaleString('zh-CN') : ''

onMounted(load)
</script>

<style scoped>
.run-list { display:flex; flex-direction:column; gap:10px; max-width:800px; }
.run-card {
  display:flex; align-items:center; gap:16px;
  padding:14px 18px; background:var(--app-surface); border:1px solid var(--line);
  border-radius:2px; cursor:pointer; transition:all .18s;
}
.run-card:hover { border-color:var(--app-ink); box-shadow:0 1px 0 var(--app-ink); }
.run-card-left { width:120px; flex-shrink:0; }
.run-time { font-size:12px; color:#909399; margin-top:4px; }
.run-progress { flex:1; display:flex; flex-direction:column; gap:6px; }
.run-counts { display:flex; gap:8px; font-size:13px; }
.cnt-pass  { color:#67c23a; font-weight:600; }
.cnt-fail  { color:#f56c6c; font-weight:600; }
.cnt-total { color:#909399; }
</style>
