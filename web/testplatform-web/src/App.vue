<template>
  <div class="app-shell">
    <!-- 侧边栏 -->
    <aside class="app-aside">
      <div class="brand">
        <div class="brand-mark">TP</div>
        <div class="brand-text">
          <div class="brand-name">Test Platform</div>
          <div class="brand-sub">UI Automation · QA</div>
        </div>
      </div>

      <el-menu :default-active="activeMenu" router class="app-menu">
        <el-menu-item index="/plans">
          <span class="nav-no">01</span>
          <el-icon><Folder /></el-icon>
          <span>测试计划</span>
        </el-menu-item>
        <el-menu-item index="/scenarios">
          <span class="nav-no">02</span>
          <el-icon><List /></el-icon>
          <span>全部场景</span>
        </el-menu-item>
        <el-menu-item index="/recording">
          <span class="nav-no">03</span>
          <el-icon><VideoCamera /></el-icon>
          <span>操作录制</span>
        </el-menu-item>
        <el-menu-item index="/history">
          <span class="nav-no">04</span>
          <el-icon><DataAnalysis /></el-icon>
          <span>执行历史</span>
        </el-menu-item>
        <el-menu-item index="/settings">
          <span class="nav-no">05</span>
          <el-icon><Setting /></el-icon>
          <span>设置</span>
        </el-menu-item>
      </el-menu>

      <div class="aside-foot">v1.0 — WPF Automation</div>
    </aside>

    <!-- 内容区 -->
    <main class="app-main">
      <router-view :key="$route.fullPath" />
    </main>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { List, DataAnalysis, VideoCamera, Folder, Setting } from '@element-plus/icons-vue'

const route = useRoute()
// 子路由（/scenarios/new、/plans/:id 等）也高亮对应顶级菜单
const activeMenu = computed(() => '/' + (route.path.split('/')[1] || 'plans'))
</script>

<style scoped>
.app-shell {
  display: flex;
  height: 100vh;
  overflow: hidden;
}

/* ── 侧边栏：暖纸浅底 + 发丝右界，编辑风 ── */
.app-aside {
  width: 244px;
  flex-shrink: 0;
  background: #faf9f5;
  border-right: 1px solid var(--line);
  display: flex;
  flex-direction: column;
}
.brand {
  display: flex;
  align-items: center;
  gap: 13px;
  padding: 26px 22px 22px;
  border-bottom: 1px solid var(--line);
}
.brand-mark {
  width: 40px; height: 40px;
  border-radius: 2px;
  background: var(--app-ink);
  color: #f5f4f0;
  display: flex; align-items: center; justify-content: center;
  font-family: var(--font-serif);
  font-size: 18px;
  font-weight: 700;
  letter-spacing: .5px;
}
.brand-name {
  color: var(--app-ink);
  font-family: var(--font-serif);
  font-size: 18px;
  font-weight: 600;
  line-height: 1.15;
}
.brand-sub {
  color: var(--app-text-mut);
  font-family: var(--font-mono);
  font-size: 10.5px;
  margin-top: 4px;
  letter-spacing: 1px;
  text-transform: uppercase;
}

.app-menu {
  flex: 1;
  border-right: none;
  background: transparent;
  padding: 16px 14px;
}
.aside-foot {
  padding: 16px 22px;
  color: var(--app-text-mut);
  font-family: var(--font-mono);
  font-size: 10.5px;
  letter-spacing: .5px;
  border-top: 1px solid var(--line);
}

/* el-menu 浅色编辑定制：左墨条激活、序号、无圆角无渐变 */
:deep(.app-menu .el-menu-item) {
  color: var(--app-text-sub);
  border-radius: 2px;
  height: 44px;
  line-height: 44px;
  margin: 2px 0;
  padding-left: 14px !important;
  font-weight: 500;
  letter-spacing: .3px;
  border-left: 2px solid transparent;
  transition: all .18s ease;
}
:deep(.app-menu .el-menu-item .nav-no) {
  font-family: var(--font-mono);
  font-size: 10px;
  color: var(--app-text-mut);
  margin-right: 11px;
  letter-spacing: .5px;
}
:deep(.app-menu .el-menu-item .el-icon) { font-size: 16px; margin-right: 8px; }
:deep(.app-menu .el-menu-item:hover) {
  background: #f0eee7;
  color: var(--app-ink);
}
:deep(.app-menu .el-menu-item.is-active) {
  background: #efece4;
  color: var(--app-ink);
  border-left: 2px solid var(--app-ink);
  font-weight: 600;
}
:deep(.app-menu .el-menu-item.is-active .el-icon),
:deep(.app-menu .el-menu-item.is-active .nav-no) { color: var(--app-ink); }

/* ── 内容区 ── */
.app-main {
  flex: 1;
  min-width: 0;
  height: 100%;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  background: var(--app-bg);
}
.app-main > * { flex: 1; min-height: 0; }
</style>
