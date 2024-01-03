# FrustumCulling

## 推荐的做法
1. 在相机上挂载 `CameraCullingGroup`，为项目约定一组 LOD 层级（按倒序排列的视口空间高度）。
2. 为需要检查可见性的物体上挂载 `CullingGroupVolume`，需要手动设置正确的本地包围盒。
3. 播放时才会检查可见性，`CullingGroupVolume` 会回传事件，在业务代码里监听它们。

## `CullingGroupVolume` 的事件
- `UnityEvent<Camera> onBecameVisible`
    - 指示物体变为可见，传递参与剔除的相机
- `UnityEvent<Camera> onBecameInvisible`
    - 指示物体变为不可见，传递参与剔除的相机
- `UnityEvent<Camera, IReadOnlyList<float>, int> lodChanged`
    - 指示 LOD 发生变化，传递参与剔除的相机、对应的 LOD 列表和 LOD 层级