# ARCHITECTURE.md

# Arquitectura

## Capas

### Dominio
Entidades, estados, reglas de negocio y objetos de valor.

No depende de persistencia, framework web, servicios externos ni interfaz.

### Aplicación
Casos de uso y coordinación de operaciones.

### Infraestructura
Persistencia, repositorios, ORM, migraciones, auditoría técnica, servicios externos y generación de comprobantes.

### Presentación
UI, endpoints, controladores, DTOs y validación de formato.

Las altas y ediciones de catálogos y entidades administrativas se presentan mediante un modal CRUD reutilizable que conserva el contexto del listado. Los flujos operativos extensos —venta, compra, pedido, recepción y toma física— permanecen como pantallas completas. Los catálogos relacionados usan modales rápidos apilables y seleccionan el registro recién creado.

Las rutas visibles usan nombres de negocio en español, minúsculas y guiones para separar palabras, sin exponer innecesariamente controladores o acciones técnicas. La ruta principal del Punto de Venta es `/pos`; las rutas anteriores se redirigen a la ruta visible canónica.

No contiene reglas centrales del negocio.

## Módulos

### Seguridad
- Usuario.
- Rol.
- Permiso.

El inicio de sesion usa ASP.NET Identity para hashing de contrasenas, salt, bloqueo por intentos fallidos y cookies de autenticacion. `LoginAuthorizationService` prepara el contexto autorizado del usuario y `AppUserClaimsPrincipalFactory` agrega permisos y sucursales autorizadas a la sesion. `PermissionAuthorizationHandler` conserva la verificacion contra la base de datos para reglas criticas.

### Catálogo
- Producto.

Un producto sin variantes es un único `Product` vendible. Un producto con variantes utiliza un `Product` base no vendible y productos hijos vendibles enlazados por `ParentProductId`; cada hijo conserva inventario, precio, costo, código y código de barras propios. Los catálogos reutilizables `VariantType`/`VariantOption` y las asociaciones `ProductVariantOption` describen la combinación de cada hijo sin duplicar la lógica de inventario, compras, ventas o Kardex. `DisplayName` concatena el nombre base y los valores de la variante.

`Product.Name` y `Product.Description` identifican el producto dentro del sistema. `Product.SupplierInvoiceName` y `Product.SupplierDescription` conservan la denominación y descripción habituales del proveedor. La búsqueda de catálogo considera estos conceptos y las interfaces de compras muestran únicamente los datos disponibles.

El código del producto puede capturarse manualmente o generarse a partir de un prefijo único de categoría y un correlativo de seis dígitos. Los códigos de variantes se derivan del código base con un sufijo correlativo de dos dígitos. Ambos quedan inmutables después de crearse.
- Categoría.

`Product` mantiene `LastCost` y `PreviousCost` como referencias operativas de costo.

La categoría y la marca son catálogos reutilizables buscables. Son opcionales durante la captura y se resuelven a `Sin categoría` y `Sin marca`. La propiedad técnica de unidad de medida se conserva por compatibilidad, pero la medida visible se modela mediante los catálogos reutilizables de atributos y valores.

### Proveedores
- Proveedor.

### Compras
- Compra.
- Detalle de compra.
- Orden de compra.
- Detalle de orden con productos y cantidades, sin información monetaria; los costos reales se capturan en la recepción final.
- PDF formal de orden de compra.
- Cuenta por pagar y pagos enlazados a la compra generada.

### Inventario
- Existencia.
- Movimiento de inventario.
- Kardex.
- Toma física basada en una sesión persistente y un detalle por producto. Cada detalle captura su referencia y versión de stock al iniciar el conteo; la finalización aplica las diferencias sobre el saldo operativo más reciente y agrega los movimientos de ajuste mediante una transacción única.

### Clientes
- Cliente.
- Consulta de NIT mediante `INitLookupService`.
- Proveedores intercambiables de consulta (`Infile`, otro certificador o captura manual).

### Ventas
- Venta.
- Detalle de venta.
- Pago.
- Anulación.
- Devolución.

### Caja
- Sesión de caja.
- Movimiento de caja.
- Arqueo y detalle por denominación.
- Cierre por dispositivo POS.
- Cajas chicas identificables, asignadas por usuario, con ciclos de apertura/cierre, movimientos y categorías reutilizables.

### Finanzas operativas
- Cuenta por pagar y pagos de proveedor.
- Cuenta por cobrar y abonos, con plazos históricos de crédito/gracia y estado temporal calculado a partir del vencimiento y el saldo.
- Forma de pago separada del origen del dinero.
- Movimientos derivados creados en la misma transacción que su documento financiero.

### Auditoría
- Bitácora.

### Configuración
- Configuración del sistema.
- Preparación FEL/INFILE con emisor reutilizado desde la configuración de empresa, ambiente, documento predeterminado, frases tributarias y credenciales protegidas.
- Forma de pago.
- Cuenta bancaria receptora.
- Catálogo de monedas y selección de una moneda operativa activa.
- Carga opcional de datos de demostración con escenarios relacionados marcados con `DEMO-`.

### Organización y Multitenant
- Grupo Empresarial (`BusinessGroup`).
- Empresa (`Company`).
- Sucursal (`Branch`).
- Bodega (`Warehouse`).
- Alcance de Usuario (`UserScopeAssignment`).
- Configuración de Empresa (`CompanySetting`) y Configuración de Sucursal (`BranchSetting`).
- Resolución jerárquica de configuración (`IEnterpriseSettingsResolver`).

### Reportes
Consultas de ventas, inventario, productos, compras y caja.

Las vistas de detalle del Dashboard reutilizan las consultas administrativas y delegan la creación de archivos `.xlsx` en `IExcelExportService`, manteniendo el formato de exportación fuera del controlador.

La consulta de Inventario reutiliza `DashboardSummary` para las clasificaciones de existencia baja, agotados y productos sin movimiento. La toma física permanece separada de la consulta de existencias y delega creación, conteo, concurrencia, finalización e historial en `IPhysicalInventoryCountService`.

## Entidades conceptuales

- BusinessGroup
- Company
- Branch
- Warehouse
- UserScopeAssignment
- CompanySetting
- BranchSetting
- User
- Role
- Permission
- Product
- ProductPriceRule
- VariantType
- VariantOption
- ProductVariantOption
- Category
- Supplier
- Purchase
- PurchaseItem
- PurchaseOrder
- PurchaseOrderItem
- InventoryStock
- InventoryMovement
- PhysicalInventoryCount
- PhysicalInventoryCountItem
- PhysicalInventoryCountCategory
- Customer
- Sale
- SaleItem
- SalePayment
- CashSession
- CashMovement
- SaleCancellation
- SaleReturn
- SaleReturnItem
- SaleRefund
- AuditLog
- PaymentMethod
- SystemSetting
- FelCertification
- BankAccount

## Relaciones principales

- `User` N..N `Role`
- `Role` N..N `Permission`
- `Product` N..1 `Category`
- `Product` 1..N `ProductPriceRule`
- `Purchase` N..1 `Supplier`
- `Purchase` 1..N `PurchaseItem`
- `PurchaseItem` N..1 `Product`
- `PurchaseOrder` N..1 `Supplier`
- `PurchaseOrder` 1..N `PurchaseOrderItem`
- `PurchaseOrderItem` N..1 `Product`
- `InventoryStock` 1..1 `Product`
- `InventoryMovement` N..1 `Product`
- `PhysicalInventoryCount` 1..N `PhysicalInventoryCountItem`
- `PhysicalInventoryCount` N..N `Category` mediante `PhysicalInventoryCountCategory`
- `PhysicalInventoryCountItem` N..1 `Product`
- `InventoryMovement` N..0..1 `PhysicalInventoryCount` y `PhysicalInventoryCountItem`
- `Sale` N..1 `User`
- `Sale` N..0..1 `Customer`
- `Sale` 1..N `SaleItem`
- `SaleItem` N..1 `Product`
- `Sale` 1..1 `SalePayment`
- `Sale` 1..0..1 `FelCertification`; sus valores de autorización, serie, número DTE, certificación, certificador, QR y acceso proceden exclusivamente de la respuesta del certificador.
- `SalePayment` N..1 `PaymentMethod`
- `Sale` N..1 `CashSession`
- `CashSession` N..1 `User`
- `CashMovement` N..1 `CashSession`
- `SaleCancellation` 1..1 `Sale`
- `SaleReturn` N..1 `Sale`
- `SaleReturn` 1..N `SaleReturnItem`
- `SaleReturnItem` N..1 `Product`
- `SaleRefund` N..1 `Sale`
- `SaleRefund` N..1 `CashSession`
- `AuditLog` N..1 `User`

## Transacciones principales

### Confirmación de venta
Validar permisos, sesión, productos, existencias, precios mediante `IProductPricingService`, descuentos y pago; persistir venta, detalle y pago; generar movimientos; actualizar existencia; auditar; confirmar.

### Confirmación de compra
La compra confirmada, directa o generada desde una recepción final, se conserva como historial de solo lectura.

### Recepción de orden de compra
Validar que el pedido esté solicitado y no tenga compra relacionada; aceptar diferencias y productos adicionales; recalcular el total real; persistir recepción, compra y detalle; generar entradas y Kardex relacionados; actualizar costos; crear cuenta por pagar solo para crédito; marcar el pedido recibido y confirmar todo con una única unidad de trabajo. La relación única `Purchase.PurchaseOrderId` actúa como resguardo de idempotencia.

### Cierre de caja
Recuperar la caja del usuario autenticado; conservar un único arqueo de cierre por denominación; calcular efectivo esperado; conciliar todos los POS asignados al abrir la caja; registrar diferencias separadas; cerrar de forma inmutable; auditar; confirmar. Abrir y cerrar la caja no solicitan nuevamente la contraseña del propietario autenticado.

### Anulación
Validar permisos y estado; registrar motivo; revertir inventario cuando corresponda; actualizar estado; auditar; confirmar.

### Devolución
Validar venta y cantidades; persistir devolución; generar entrada cuando corresponda; actualizar estado; auditar; confirmar.

## Extensibilidad

Permitir incorporación futura de multisucursal, cotizaciones, promociones, e-commerce y otras integraciones. FEL dispone únicamente de configuración segura y almacenamiento de la futura certificación por venta; el conector y la emisión real permanecen pendientes hasta contar con el contrato técnico y credenciales de INFILE.

La consulta de NIT separa la búsqueda local de los conectores externos. `INitLookupService` coordina la resolución y `INitLookupProvider` representa cada integración. Los conectores de Infile y otros certificadores permanecen deshabilitados hasta recibir URL, contrato y credenciales mediante configuración segura.




## Operación offline

La capacidad offline aplica únicamente al Punto de Venta. El navegador conserva en IndexedDB el catálogo mínimo, la caja activa y las ventas pendientes; el Service Worker mantiene los recursos necesarios para abrir `/pos` y el monitor `/Pos/Offline`. Al recuperar conexión, la API sincroniza cada venta mediante `ClientOperationId`, valida otra vez las reglas del servidor y registra la fecha local y la fecha de sincronización.

Compras, inventario administrativo, configuración, usuarios, permisos, reportes y demás módulos continúan requiriendo comunicación directa con el servidor. Las aprobaciones de descuentos también requieren conexión y una venta con descuento no puede guardarse offline. El detalle del flujo y sus límites se define en `OFFLINE_SYNC.md`.


## Despliegue

La arquitectura de infraestructura y red se define en `AWS_DEPLOYMENT.md`.

### Actualización del maestro de restaurante

`CatalogService` valida el umbral propio de inactividad, la unidad base y el uso histórico antes de guardar o eliminar un producto. `CatalogRepository` consulta las relaciones operativas existentes; las futuras recetas deberán incorporarse a esa protección. `NoMovementDaysOverride` conserva su nombre de persistencia por compatibilidad, pero ya no representa una excepción a una configuración global.

`InventoryLotAllocation` determina FIFO/FEFO en dominio. `InventoryLotLedger` coordina lotes y asignaciones al persistir nuevos movimientos en `AppDbContext`, de modo que existencias, documentos, lotes y trazabilidad se guarden en la misma transacción de EF Core. La concurrencia optimista de existencias y lotes usa `xmin`. `InventoryMovementLot` conserva cantidades firmadas y referencias a la asignación original en reintegros. El nombre de tabla `InventoryExpiryLots` se mantiene por compatibilidad, aunque admite lotes sin vencimiento.

`InventoryLotService` consulta por bodega autorizada, registra apertura sobre saldo no asignado bajo aislamiento serializable y permite bajas explícitas. El maestro de presentaciones se conecta con la captura de compras mediante identificadores, resolviendo factores en backend y guardando la fotografía en `PurchaseItem`.
