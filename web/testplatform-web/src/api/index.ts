import axios from 'axios'

const api = axios.create({ baseURL: 'http://localhost:5000/api' })

export const planApi = {
  list:            ()              => api.get('/plans'),
  get:             (id: string)   => api.get(`/plans/${id}`),
  create:          (data: any)    => api.post('/plans', data),
  update:          (id: string, data: any) => api.put(`/plans/${id}`, data),
  delete:          (id: string)   => api.delete(`/plans/${id}`),
  getScenarios:    (id: string)   => api.get(`/plans/${id}/scenarios`),
  addScenarios:    (id: string, scenarioIds: string[]) => api.post(`/plans/${id}/scenarios`, scenarioIds),
  removeScenario:  (id: string, sid: string) => api.delete(`/plans/${id}/scenarios/${sid}`),
  run:             (id: string, data: any) => api.post(`/plans/${id}/run`, data),
  cancelRun:       (runId: string)=> api.post(`/plans/runs/${runId}/cancel`),
  getRuns:         (id: string)   => api.get(`/plans/${id}/runs`),
  getRunItems:     (runId: string)=> api.get(`/plans/runs/${runId}/items`),
  scenarioStatus:  (id: string)   => api.get(`/plans/${id}/scenario-status`)
}

export const scenarioApi = {
  list: () => api.get('/scenarios'),
  get: (id: string) => api.get(`/scenarios/${id}`),
  create: (data: any) => api.post('/scenarios', data),
  update: (id: string, data: any) => api.put(`/scenarios/${id}`, data),
  delete: (id: string) => api.delete(`/scenarios/${id}`)
}

export const recordingApi = {
  start:  (windowTitle: string, type: string = 'wpf') => api.post('/recording/start', { windowTitle, type }),
  stop:   ()                    => api.post('/recording/stop'),
  clear:  ()                    => api.post('/recording/clear'),
  saveTo: (scenarioId: string, steps: any[]) =>
    api.post(`/recording/save-to/${scenarioId}`, { steps })
}

export const settingsApi = {
  get:  ()           => api.get('/settings'),
  save: (data: Record<string, string>) => api.put('/settings', data)
}

export const systemApi = {
  windows:      () => api.get('/system/windows'),
  logsInfo:     () => api.get('/system/logs/info'),
  logsCleanup:  (days: number) => api.post('/system/logs/cleanup', null, { params: { days } })
}

export const taskApi = {
  run: (scenarioId: string, inputParams: Record<string, string>, mode: string = 'auto') =>
    api.post('/tasks/run', { scenarioId, inputParams, mode }),
  cancel: (runId: string) => api.post(`/tasks/${runId}/cancel`),
  history: (scenarioId?: string) =>
    api.get('/tasks/history', { params: scenarioId ? { scenarioId } : {} }),
  logs: (runId: string) => api.get(`/tasks/${runId}/logs`),
  status: (runId: string) => api.get(`/tasks/${runId}/status`)
}
