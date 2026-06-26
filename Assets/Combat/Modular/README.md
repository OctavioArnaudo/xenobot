# Modular Combat

Sistema separado para agregar disparo, daño, VFX de impacto e IA básica sin modificar directamente los prefabs del player o de los enemies.

## Prefabs

- `Prefabs/PlayerShootModule.prefab`: módulo base para disparo por click.
- `Prefabs/EnemyAttackModule.prefab`: módulo base con daño, disparo e IA básica.
- `Prefabs/ModularProjectile.prefab`: proyectil visible con trail, sphere cast, daño e impacto visual.
- `Prefabs/ModularImpactVfx.prefab`: ráfaga procedural de partículas para impactos y muertes.

## Player

1. Abre tu prefab de player.
2. Agrega `CombatTeamMember` al root y pon `Team = Player`.
3. Agrega `ClickToShoot` al root o arrastra `PlayerShootModule.prefab` como referencia para copiar sus valores.
4. Asigna:
   - `Aim Camera`: la cámara del jugador.
   - `Muzzle`: un transform hijo en la punta del arma.
   - `Projectile Prefab`: `ModularProjectile.prefab`.

## Enemy

1. Abre tu prefab de enemy.
2. Agrega `CombatTeamMember` al root y pon `Team = Enemy`.
3. Agrega `CombatDamageReceiver` si el enemigo no usa ya `Damageable`, `Health` o `enemyHealth`.
4. Para IA básica, agrega `ClickToShoot` y `BasicAttackAI`.
5. Asigna `Muzzle` y `Projectile Prefab` en `ClickToShoot`.

`BasicAttackAI` usa `NavMeshAgent` si existe; si no existe, mueve el transform directamente. Busca targets con tag `Player`.

## Compatibilidad

El daño intenta usar, en este orden:

1. `Damageable` del sistema FPS existente.
2. `CombatDamageReceiver` del módulo nuevo.
3. Métodos `TakeDamage(int)` como `enemyHealth`.
4. Métodos `ApplyDamageRpc(int)` como `PlayerHealth`.
