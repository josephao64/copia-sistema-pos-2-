# SISTEMI POS Performance Seeder

Generador manual y determinístico de datos sintéticos. Solo acepta una conexión local cuyo nombre de base sea exactamente `SistemiPosRestaurantePerformanceDb`; no aplica migraciones ni modifica el esquema.

Configure la conexión mediante una variable de entorno y ejecute el proyecto explícitamente:

```powershell
$env:SISTEMI_POS_RESTAURANTE_PERFORMANCE_DB_CONNECTION = 'Host=localhost;Port=5432;Database=SistemiPosRestaurantePerformanceDb;Username=postgres;Password=CHANGE_ME'
dotnet run --project tools\SistemiPos.PerformanceSeeder\SistemiPos.PerformanceSeeder.csproj -c Release --no-build
```

Parámetros opcionales:

```text
--products 10000
--customers 2000
--suppliers 400
--sales 5000
--purchases 2000
--inventory-movements 50000
--reset
```

Sin `--reset`, el generador rechaza una base que ya tenga datos. `--reset` conserva `__EFMigrationsHistory`, el esquema y las presentaciones incluidas por la migración inicial; solo puede ejecutarse después de superar las validaciones de host local y nombre exacto de base.
