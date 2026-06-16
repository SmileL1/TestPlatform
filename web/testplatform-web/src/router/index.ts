import { createRouter, createWebHistory } from 'vue-router'
import PlanList        from '../views/PlanList.vue'
import PlanDetail      from '../views/PlanDetail.vue'
import PlanRunView     from '../views/PlanRunView.vue'
import PlanHistoryView from '../views/PlanHistoryView.vue'
import ScenarioList  from '../views/ScenarioList.vue'
import ScenarioEdit  from '../views/ScenarioEdit.vue'
import TaskMonitor   from '../views/TaskMonitor.vue'
import HistoryView   from '../views/HistoryView.vue'
import RecordingView from '../views/RecordingView.vue'
import SettingsView  from '../views/Settings.vue'

export default createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/',                              redirect: '/plans'       },
    { path: '/plans',                         component: PlanList      },
    { path: '/plans/:id',                     component: PlanDetail    },
    { path: '/plans/:id/history',             component: PlanHistoryView },
    { path: '/plans/:planId/run/:runId',      component: PlanRunView   },
    { path: '/scenarios',           component: ScenarioList },
    { path: '/scenarios/new',       component: ScenarioEdit },
    { path: '/scenarios/:id/edit',  component: ScenarioEdit },
    { path: '/monitor/:runId',      component: TaskMonitor  },
    { path: '/history',             component: HistoryView  },
    { path: '/recording',           component: RecordingView},
    { path: '/settings',            component: SettingsView }
  ]
})
