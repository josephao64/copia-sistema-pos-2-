# REQUIREMENTS.md

# Requerimientos funcionales

## Seguridad

### RF-SEG-001
Requerir autenticación para acceder al sistema.

### RF-SEG-002
Administrar usuarios, roles y permisos.

### RF-SEG-003
Controlar permisos por módulo y acción.

### RF-SEG-004
Aplicar autorización en backend.

### RF-SEG-005
Separar el acceso a módulos por rol:

- `Super Administrador`: acceso total al dashboard, ventas, caja, clientes, catálogo, inventario, compras, proveedores, reportes, usuarios, roles, permisos y configuración del sistema.
- `Administrador`: dashboard, catálogo, inventario, compras, proveedores, clientes, caja y reportes.
- `Gerente`: dashboard, consultas de catálogo, inventario, compras, proveedores, clientes, caja y reportes.
- `Punto de Venta`: ventas, caja, clientes necesarios para vender y operación offline del POS.

La creación de una nueva venta está reservada exclusivamente al rol `Punto de Venta` y además requiere el permiso operativo correspondiente. El historial de ventas puede consultarse por los perfiles autorizados para supervisión. `Administrador` y `Gerente` no deben crear ventas mediante la interfaz ni por URL directa. Los reportes agregados permanecen disponibles para los roles administrativos autorizados a reportes.

### RF-SEG-006
El `Super Administrador` debe poder editar las credenciales de los usuarios: nombre de usuario, nombre completo, correo y contraseña. Al editar, la nueva contraseña es opcional; si se deja vacía debe conservarse la contraseña actual. Toda nueva contraseña debe cumplir la política de seguridad configurada y debe invalidar las credenciales de sesión anteriores del usuario afectado.

### RF-SEG-007
El `Super Administrador` debe poder crear roles personalizados y asignarles permisos de módulos operativos. El acceso de estos roles debe validarse por permiso tanto en la interfaz como en backend.

Los roles no deben presentarse como un módulo independiente en la navegación. Su creación y la asignación de capacidades deben centralizarse en la pantalla `Permisos`.

La administración de usuarios, roles, permisos y configuración permanece reservada al `Super Administrador`. La acción `Nueva venta` permanece reservada exclusivamente a `Punto de Venta`, aunque otro rol reciba accidentalmente permisos de ventas.

Los roles administrativos integrados son `Super Administrador` y `Administrador`; el nombre heredado en inglés `Administrator` no representa un rol adicional y debe retirarse cuando no tenga usuarios asignados.

### RF-SEG-008
Tras cinco intentos fallidos de inicio de sesión, la cuenta se bloquea temporalmente durante quince minutos. Los fallos de autenticación deben responder con el mensaje genérico `Credenciales incorrectas`, sin indicar si falló el usuario o la contraseña. Cuando la cuenta esté bloqueada, se informa el bloqueo temporal sin revelar detalles adicionales de la credencial.

### RF-SEG-009
Cada inicio de sesión exitoso debe actualizar el último acceso del usuario (`LastLoginAtUtc`) y registrar el evento en la auditoría de seguridad, incluyendo cierre de sesión, intentos fallidos y bloqueos temporales. La consulta de estos registros requiere el permiso `security.audit.view`. El último acceso debe mostrarse en el perfil del usuario y en el listado administrativo de usuarios.

### RF-SEG-010
La cookie de autenticación expira según la configuración operativa (ocho horas por defecto, con renovación deslizante). Cuando la sesión expire, el sistema debe redirigir al inicio de sesión mostrando el indicador `Tu sesión ha expirado. Inicia sesión nuevamente.` Las solicitudes AJAX o API no autenticadas deben responder HTTP 401 y el cliente debe informar la expiración de sesión.

## Productos

### RF-PROD-001
Crear, editar, consultar y desactivar productos.

### RF-PROD-002
Cada producto debe almacenar:

- Código del producto permanente.
- Código de barras.
- Nombre en el sistema, utilizado como identificación principal en ventas, inventario y listados generales.
- Nombre en factura/proveedor, conservado como referencia para compras, pedidos y recepción.
- Descripción.
- Categoría.
- Marca.
- Medida mediante atributos y valores reutilizables.
- Último costo de compra.
- Costo anterior.
- Precio de venta.
- Stock mínimo.
- Estado.

### RF-PROD-003
Buscar por:

- Código interno.
- Código de barras.
- Nombre.
- Nombre en factura/proveedor.

La lista debe filtrarse conforme se escribe, sin distinguir mayúsculas, minúsculas ni tildes. El mismo comportamiento se aplica a los listados administrativos de usuarios, cajas, clientes, proveedores, compras y pedidos.

### RF-PROD-004
Consultar existencia actual.

### RF-PROD-004A — Atributos y variantes reutilizables
Un producto puede operar sin variantes como un único SKU o agrupar varios SKU hijos. Los atributos y valores pertenecen a catálogos reutilizables; cada combinación genera una variante con código, código de barras, precio, costo e inventario propios. Las compras seleccionan esos mismos SKU y pueden crear el producto y sus variantes desde el formulario reutilizado del catálogo.

El código base admite captura manual exacta o generación por categoría con prefijo único y seis dígitos. Al editar, código, categoría, marca y tipo de producto son de solo lectura. Categoría y marca son opcionales y utilizan `Sin categoría` y `Sin marca` como valores predeterminados. Atributos y valores se seleccionan mediante búsqueda con autocompletado y admiten crear opciones nuevas desde la misma sección. La creación de atributos y valores se realiza con botones separados y modales; al guardar, el elemento se incorpora al catálogo y queda seleccionado.

### RF-PROD-005 — Multiprecios
Cada producto mantiene un precio de venta base y puede habilitar opcionalmente reglas dinámicas de multiprecio. Los productos existentes y los productos con multiprecio deshabilitado utilizan únicamente el precio base.

Modos admitidos:

- `Cantidad mínima`: aplica a todas las unidades de la línea el precio unitario de la regla activa con la mayor cantidad menor o igual a la cantidad vendida.
- `Cantidad exacta`: aplica el precio total especial únicamente cuando la cantidad de la línea coincide exactamente con una regla activa.

Las reglas contienen cantidad, precio y estado activo. No se permiten cantidades repetidas por producto; la cantidad debe ser mayor que cero y el precio no puede ser negativo. Deshabilitar multiprecios no elimina sus reglas.

Nueva Venta debe permitir separar un mismo producto en varias líneas de una sola venta para combinar una cantidad exacta con unidades al precio base. Todas las líneas descuentan el mismo inventario físico del producto.

## Categorías

### RF-CAT-001
Crear, editar, consultar y desactivar categorías.

La creación de categorías se ofrece desde el formulario de nuevo producto. Categorías no ocupa una opción independiente en la navegación lateral.

## Proveedores

### RF-PRV-001
Crear, editar, consultar y desactivar proveedores.

### RF-PRV-002
Registrar:

- Nombre o razón social.
- NIT.
- Nombre comercial.
- Teléfono.
- Correo.
- Dirección.
- Contacto.
- Estado.

### RF-PRV-003
Consultar historial de compras por proveedor.

## Compras

### RF-COM-001
El historial reúne compras confirmadas directas y compras generadas automáticamente al confirmar la recepción final de una orden; nunca incluye borradores.

### RF-COM-002
La compra debe almacenar:

- Número.
- Proveedor.
- Fecha.
- Usuario.
- Estado.
- Total.

### RF-COM-003
Cada línea debe almacenar:

- Producto.
- Cantidad.
- Costo unitario.
- Subtotal.

### RF-COM-004
La recepción final confirma automáticamente la compra relacionada.

### RF-COM-005
La confirmación debe aumentar inventario.

### RF-COM-006
Conservar el costo histórico de cada línea.

### RF-COM-007
Consultar historial de compras.

### RF-COM-008 — Órdenes de compra
Permitir registrar y editar órdenes de compra con número, proveedor, fecha de creación del sistema, productos, cantidades solicitadas, comentario general y estado. La orden no captura ni almacena costos, subtotales o totales estimados.

Proveedor y producto se seleccionan mediante búsquedas con autocompletado. En compras y pedidos, la búsqueda considera nombre y descripción del sistema y nombre y descripción del proveedor; solo se muestran los datos disponibles. La fecha de creación se establece en backend usando la hora del sistema y se presenta como información compacta de solo lectura. Un producto solo puede aparecer una vez por pedido.

Estados permitidos: `Borrador`, `Solicitado`, `Recibido` y `Cancelado`. Borradores y solicitados son editables; recibidos y cancelados son de solo lectura. Un pedido solicitado permite generar, previsualizar, descargar e imprimir su PDF formal con datos de empresa, proveedor, productos y cantidades, sin información monetaria.

### RF-COM-009
Cada pedido solicitado admite una única recepción final. Esta captura factura o documento, fecha real, condición de contado o crédito, días de crédito, cantidades y costos reales, diferencias y productos adicionales. Puede recibirse menos o más de lo solicitado. La confirmación es transaccional e idempotente: marca el pedido `Recibido`, aumenta inventario, registra Kardex, genera una compra histórica confirmada y crea cuenta por pagar únicamente cuando la condición es crédito.

### RF-COM-010
Mostrar los días transcurridos desde la fecha de solicitud y conservar los pedidos `Solicitado` para consulta en Dashboard. El detalle recibido conserva vínculos al pedido, recepción, compra, movimientos y cuenta por pagar cuando aplique.

## Inventario

### RF-INV-001
Consultar existencia actual por producto.

### RF-INV-002
Registrar movimientos de:

- Entrada por compra.
- Salida por venta.
- Ajuste positivo.
- Ajuste negativo.
- Devolución.
- Reversión por anulación.

### RF-INV-003
Cada movimiento debe almacenar:

- Producto.
- Tipo.
- Cantidad.
- Existencia anterior.
- Existencia nueva.

### RF-INV-004 — Toma física
La sección Inventario debe permitir crear sesiones de toma física para todo el inventario o para una o varias categorías, continuar sesiones en proceso y consultar su historial. Los productos quedan definidos al crear la sesión y no pueden participar en otra toma activa.

Cada producto debe capturar su propia existencia de referencia y versión de stock al comenzar el conteo. Guardar la fila conserva la fotografía histórica, muestra la diferencia y el estado visual, admite comentario opcional y no modifica el inventario operativo. Si el stock cambia antes de guardar, debe solicitarse reconteo sin bloquear ventas, compras ni otros movimientos.

La toma solo puede finalizar sin pendientes ni reconteos. La finalización debe ejecutarse transaccionalmente, volver a obtener la existencia operativa y aplicar únicamente `existencia actual + diferencia del conteo`. Solo las diferencias distintas de cero generan ajustes de Kardex relacionados con la toma. Las tomas finalizadas son inmutables y pueden exportarse a Excel con encabezado, detalle y resumen.
- Fecha y hora.
- Usuario.
- Documento origen.
- Observación.

### RF-INV-004
Consultar Kardex por producto.

### RF-INV-005
Mostrar productos con existencia igual o inferior al stock mínimo.

### RF-INV-006
Impedir cambios de existencia sin movimiento de inventario.

## Clientes

### RF-CLI-001
Crear, editar, consultar y desactivar clientes.

### RF-CLI-002
Registrar:

- Nombre.
- NIT.
- Teléfono.
- Correo.
- Dirección.
- Estado.

### RF-CLI-003
Permitir ventas a consumidor final.

### RF-CLI-004
Consultar historial de compras por cliente.

### RF-CLI-005
En Nueva Venta, al ingresar un NIT el sistema debe consultar primero los clientes almacenados localmente. Si existe un cliente activo, debe completar automáticamente su nombre y reutilizar su identificador.

### RF-CLI-006
Cuando el NIT no exista localmente, el sistema debe consultar el proveedor configurado mediante la abstracción `INitLookupService`. Si no existe un proveedor operativo, no tiene credenciales, no encuentra el NIT o falla la consulta, debe permitir ingresar la razón social manualmente.

### RF-CLI-007
Cada cliente conserva días de crédito y días adicionales de gracia. Al confirmar una venta a crédito, el vencimiento se calcula con los días de crédito configurados y ambos plazos se copian a la cuenta por cobrar como datos históricos.

### RF-CLI-008
Cuentas por cobrar separa cuentas con saldo en General, Vencidas, Morosas, Sin abonos y Parcialmente abonadas; Historial contiene únicamente cuentas pagadas. Vencida corresponde al período posterior al vencimiento y dentro de la gracia; Morosa comienza al agotarse también la gracia.

### RF-CLI-007
Al confirmar una venta con NIT y razón social de un cliente no existente, el cliente debe almacenarse en `Customers` dentro de la misma operación para poder reutilizarlo posteriormente.

## Punto de venta

### RF-POS-001
Crear ventas con uno o varios productos.

### RF-POS-002
Agregar productos por:

- Código interno.
- Código de barras.
- Nombre.

### RF-POS-003
Mostrar por línea:

- Producto.
- Cantidad.
- Precio unitario.
- Descuento.
- Subtotal.

### RF-POS-004
Modificar cantidades antes de confirmar.

### RF-POS-005
Aplicar descuentos según permisos.

### RF-POS-006
Mostrar subtotal, descuento y total.

### RF-POS-007
Seleccionar cliente registrado o consumidor final.

### RF-POS-008
Seleccionar una sola forma de pago:

- Efectivo.
- Transferencia.
- POS.

### RF-POS-009
La venta debe almacenar:

- Número.
- Fecha y hora.
- Cajero.
- Cliente cuando aplique.
- Detalle.
- Subtotal.
- Descuento.
- Total.
- Forma de pago.
- Estado.

### RF-POS-010
Confirmar venta.

### RF-POS-011
La confirmación debe descontar inventario.

### RF-POS-012
Generar comprobante de venta.

### RF-POS-013
Conservar el precio histórico de cada línea.

## Pagos

### RF-PAY-001 — Efectivo
Registrar total, monto recibido y cambio.

Impedir confirmación cuando el monto recibido sea menor al total.

### RF-PAY-002
Permitir incorporar nuevas formas de pago en el futuro sin reescribir la lógica central de ventas.

## Caja

### RF-CAJ-001
Abrir caja registrando fecha y hora, cajero y monto inicial.

### RF-CAJ-002
Asociar cada venta a una sesión de caja activa.

### RF-CAJ-003
Separar ventas en efectivo, transferencia, POS, otros ingresos y egresos autorizados.

### RF-CAJ-004
Cerrar caja mostrando monto inicial, total vendido, total por forma de pago, otros ingresos, egresos, efectivo esperado, efectivo contado y diferencia.

### RF-CAJ-005
Conservar historial de aperturas y cierres.

## Comprobante de venta

### RF-CMP-001
El sistema debe permitir generar comprobante en:

- Ticket térmico.
- PDF.

### RF-CMP-002
El comprobante debe incluir:

- Nombre del negocio.
- NIT cuando esté configurado.
- Dirección y teléfono cuando estén configurados.
- Número de venta.
- Fecha y hora.
- Cajero.
- Cliente o consumidor final.
- Productos.
- Cantidad.
- Precio unitario.
- Subtotal.
- Descuento.
- Total.
- Forma de pago.
- Referencia cuando corresponda.
- Cambio cuando corresponda.
- Leyenda configurable.

### RF-CMP-003
Administración debe permitir configurar si el impuesto se muestra o se oculta en el comprobante.

### RF-CMP-004
Cuando la opción esté habilitada, el comprobante debe mostrar el desglose de impuesto.

### RF-CMP-005
Cuando la opción esté deshabilitada, el comprobante no debe mostrar el impuesto, pero el cálculo y almacenamiento interno deben mantenerse.

## Reembolsos

### RF-REF-001
El sistema debe impedir ejecutar un reembolso sin autorización de un usuario con permiso de alto nivel.

### RF-REF-002
El medio de devolución debe poder seleccionarse independientemente del medio de pago original.

### RF-REF-003
El reembolso debe registrar:

- Venta original.
- Monto.
- Medio de devolución.
- Motivo.
- Usuario que procesa.
- Usuario que autoriza.
- Fecha y hora.
- Referencia cuando corresponda.

### RF-REF-004
El sistema debe mostrar el reembolso en la sesión de caja correspondiente.

### RF-REF-005
El cierre de caja debe presentar los reembolsos separados por medio de devolución.

### RF-REF-006
Los reembolsos en efectivo deben disminuir el efectivo esperado.

### RF-REF-007
Los reembolsos por transferencia o POS/tarjeta deben quedar registrados para conciliación y auditoría.

### RF-REF-008
La venta original debe conservarse íntegra y relacionada con el reembolso.

### RF-REF-009
La autorización y ejecución del reembolso deben quedar auditadas.

## Control de sesiones de caja

### RF-CAJ-010
El sistema debe impedir que un usuario tenga más de una sesión de caja activa simultáneamente.

### RF-CAJ-011
La caja pertenece al usuario autenticado y no a una terminal, computadora o punto físico. Cerrar la sesión web no cierra la sesión de caja.

### RF-CAJ-012
Una sesión cerrada no debe poder reabrirse directamente.

### RF-CAJ-013
Las correcciones posteriores al cierre deben realizarse mediante movimientos o ajustes auditados.

### RF-CAJ-014
Cada ajuste debe registrar:

- Usuario.
- Fecha y hora.
- Motivo.
- Monto.
- Tipo de ajuste.
- Sesión relacionada.

### RF-CAJ-015
El cierre original debe conservarse sin modificaciones destructivas.

## Pago con POS / tarjeta

### RF-CARD-001
Al seleccionar POS/tarjeta como forma de pago, el sistema debe solicitar:

- Tipo de tarjeta.
- Últimos 4 dígitos.
- Número de autorización.
- Número de voucher o referencia.
- Monto.

### RF-CARD-002
Los tipos iniciales de tarjeta serán:

- Débito.
- Crédito.

### RF-CARD-003
El pago debe manejar los estados:

- `Approved`.
- `Rejected`.
- `Pending`.

### RF-CARD-004
El sistema no debe almacenar:

- Número completo de tarjeta.
- CVV.
- PIN.
- Datos sensibles de autenticación.

### RF-CARD-005
El sistema debe registrar el usuario que creó el pago.

### RF-CARD-006
El estado y los datos de autorización deben conservarse históricamente con la venta.

### RF-CARD-007
La confirmación de una venta con POS/tarjeta requiere que el pago se encuentre en estado `Approved`.

### RF-CARD-008
El catálogo de dispositivos POS debe registrar nombre, banco o proveedor, Terminal ID, cuenta bancaria asociada, ubicación o caja, observación opcional y estado.

### RF-CARD-009
El sistema debe permitir consultar el total vendido por dispositivo POS y conservar la asociación del dispositivo con cada venta pagada con tarjeta.

## Transferencias

### RF-TRF-001
Al seleccionar transferencia como forma de pago, el sistema debe solicitar:

- Banco origen.
- Banco y cuenta destino.
- Referencia.
- Monto.
- Fecha de transferencia.

### RF-TRF-002
Administración debe permitir mantener un catálogo de cuentas bancarias receptoras del negocio.

Cada cuenta debe permitir como mínimo:

- Banco.
- Identificador o nombre de cuenta.
- Número de cuenta en formato protegido o visible según permisos.
- Estado activo/inactivo.

### RF-TRF-003
El Punto de Venta debe permitir seleccionar una cuenta destino activa desde configuración.

### RF-TRF-004
La transferencia debe manejar los estados:

- `Pending`.
- `Received`.
- `Verified`.
- `Rejected`.

### RF-TRF-005
El sistema debe registrar quién creó la operación.

### RF-TRF-006
La verificación o rechazo debe registrar:

- Usuario.
- Fecha y hora.
- Observación cuando corresponda.

### RF-TRF-007
La acción de verificar o rechazar una transferencia debe estar protegida por permiso.

### RF-TRF-008
Una transferencia se registra directamente en el servidor con estado `Received` y puede verificarse posteriormente.

### RF-TRF-009
El estado `Received` permite confirmar la venta. El estado `Verified` corresponde a una validación posterior de conciliación y auditoría.

## Descuentos

### RF-DES-001
Administración debe permitir habilitar o deshabilitar la capacidad de aplicar descuentos.

### RF-DES-002
La configuración de descuento debe permitir definir un porcentaje máximo permitido.

### RF-DES-003
El porcentaje máximo debe poder configurarse por rol.

### RF-DES-004
Opcionalmente debe poder configurarse un límite específico por usuario, prevaleciendo sobre el límite del rol.

### RF-DES-005
Si el usuario no tiene permiso de descuento, la interfaz y el backend deben impedir aplicarlo.

### RF-DES-006
Al cobrar una venta con descuento, el sistema debe crear una solicitud pendiente y requerir la aprobación de otro usuario con el permiso configurable de autorizar descuentos.

### RF-DES-007
El sistema debe registrar auditoría del descuento y de la autorización cuando corresponda.

### RF-DES-008
En el Punto de Venta, los descuentos por producto deben admitir porcentaje y monto fijo y recalcular inmediatamente los totales. El vendedor puede ingresarlos sin credenciales de un superior; al cobrar, la solicitud debe mostrar al autorizador los productos, cantidades, precios, tipo, valor, comentario y monto descontado. El modal de cobro debe permanecer abierto y bloqueado mientras espera la decisión y, al aprobarse, continuar automáticamente con la venta. El backend debe impedir confirmar la venta mientras la solicitud esté pendiente o rechazada, y debe invalidar la aprobación si cambia el carrito.

## Anulaciones

### RF-ANU-001
Permitir anular una venta confirmada.

### RF-ANU-002
Registrar motivo, usuario, fecha y hora.

### RF-ANU-003
Revertir inventario cuando corresponda.

### RF-ANU-004
Conservar la venta original.

## Devoluciones

### RF-DEV-001
Permitir devolución total o parcial.

### RF-DEV-002
Registrar motivo, usuario, fecha y hora.

### RF-DEV-003
Generar entrada de inventario cuando el producto regrese físicamente.

### RF-DEV-004
Conservar relación con la venta original.

## Bitácora

### RF-AUD-001
Registrar usuario, fecha y hora, acción, módulo, registro afectado y documento relacionado cuando aplique.

### RF-AUD-002
Auditar anulaciones, devoluciones, descuentos, ajustes de inventario, cambios de precio, aperturas y cierres de caja y cambios administrativos.

## Reportes

### RF-REP-001 — Ventas
- Día.
- Rango de fechas.
- Cajero.
- Producto.
- Categoría.
- Forma de pago.

### RF-REP-002 — Inventario
- Existencias.
- Stock bajo.
- Agotados.
- Kardex.
- Entradas.
- Salidas.
- Ajustes.

La consulta de Inventario debe ofrecer las mismas clasificaciones de existencia baja, agotados y productos sin movimiento del Dashboard. La toma física se realiza en una pestaña dedicada mediante sesiones persistentes; la existencia contada se captura como un número entero igual o mayor que cero y las diferencias se aplican al finalizar la sesión conforme a RF-INV-004.

### RF-REP-003 — Productos
- Más vendidos.
- Menos vendidos.
- Sin movimiento.

### RF-REP-004 — Compras
- Proveedor.
- Rango de fechas.
- Producto.

### RF-REP-005 — Caja
- Aperturas.
- Cierres.
- Diferencias.
- Ingresos.
- Egresos.

## Dashboard

### RF-DASH-001
El Dashboard administrativo muestra únicamente: ventas netas de hoy, ventas por caja, productos con existencia baja sin incluir agotados, productos agotados, pedidos pendientes y productos sin movimiento.

Las ventas del día excluyen anulaciones y descuentan los reembolsos registrados durante el día. Debe mostrar la hora de última actualización y permitir consultar nuevamente los datos mediante `Actualizar`.

### RF-DASH-002
Los productos sin movimiento se calculan desde la última venta válida. Para productos nunca vendidos se usa su primer ingreso de inventario o, si no existe, su fecha de creación.

### RF-DASH-003
Los detalles de existencia baja, agotados, pedidos pendientes y productos sin movimiento deben abrirse en una vista independiente, permitir regresar al Dashboard, buscar dentro del listado y exportar todos los registros a un archivo Excel `.xlsx`.

## Configuración

### RF-CONF-001
Configurar datos del negocio, nombre comercial, NIT, dirección, teléfonos, logo, moneda, impuestos, formas de pago, parámetros de inventario y caja.

### RF-CONF-002 — Moneda
Permitir configurar la moneda operativa con:

- Código.
- Nombre.
- Símbolo.
- Cantidad de decimales.
- Separador decimal.
- Separador de miles.
- Posición del símbolo.

### RF-CONF-003
La moneda inicial por defecto puede ser `GTQ`, pero debe poder cambiarse desde administración sin modificar código.

### RF-CONF-004
Las transacciones deben conservar la moneda y el formato monetario relevante para su histórico. Un cambio posterior de configuración no debe recalcular ventas, compras, reembolsos ni cierres anteriores.

### RF-CONF-005
El MVP maneja una moneda operativa activa. La configuración de moneda no implica soporte multi-moneda.

### RF-CONF-006
Permitir seleccionar el proveedor de consulta de NIT entre `Ninguno / Manual`, `Infile` y `Otro`. Las credenciales y tokens de proveedores externos no deben almacenarse en el código fuente; se suministran mediante configuración segura cuando se implemente el conector específico.

### RF-CONF-007
Configurar los días sin movimiento directamente en cada producto (1 a 3650). No existe herencia de Configuración General. Los productos antiguos sin valor se excluyen de la alerta hasta que se configure al editarlos.

### RF-CONF-008 — Preparación FEL / INFILE
La pestaña Facturación debe separar Emisor, FEL/INFILE y Documento. Los datos del emisor reutilizan la configuración de Empresa; las credenciales se conservan cifradas y no se muestran nuevamente en texto plano.

### RF-CONF-009 — Resultado de certificación
El modelo debe permitir una certificación FEL por venta. Autorización, serie, número DTE, fecha de certificación, certificador, QR y número de acceso solo pueden proceder de la respuesta de INFILE y nunca se generan localmente.

## Compatibilidad de acceso

### RNF-UI-001
La aplicación debe ser utilizable desde:

- Desktop.
- Laptop.
- Tablet.
- Teléfono.

### RNF-UI-002
La interfaz debe ser responsive.

### RNF-UI-003
El punto de venta debe ser compatible con lectores de código de barras que operen como entrada de teclado.

### RNF-UI-004 — Mensajes y notificaciones
Todos los mensajes visibles del sistema deben estar en español, usar texto claro orientado al usuario y presentarse mediante componentes visuales estandarizados:

- Toast para confirmaciones, información, advertencias y errores generales derivados de una acción.
- Validación inline para errores asociados directamente con campos de formularios.
- Modal o diálogo de confirmación para operaciones importantes, sensibles, irreversibles o que requieran una decisión explícita.
- Banner para advertencias o problemas generales que afecten una pantalla, un módulo o el sistema completo.

Los componentes deben admitir, como mínimo, los estados éxito, información, advertencia y error. No se permite utilizar `alert()` del navegador ni mostrar detalles técnicos directamente al usuario.


## Código de barras

### RF-BAR-001
El código de barras debe ser opcional al registrar un producto.

### RF-BAR-002
Cuando se registre un código de barras, el sistema debe validar que no exista otro producto con el mismo valor.

### RF-BAR-003
El Punto de Venta debe permitir buscar productos por:

- Código de barras.
- Código interno.
- Nombre.

### RF-BAR-004
La ausencia de código de barras no debe impedir vender el producto.

## Numeración de documentos

### RF-NUM-001
El sistema debe generar numeración independiente por tipo de documento.

### RF-NUM-002
Cada número debe componerse de:

- Prefijo o serie del tipo de documento.
- Correlativo secuencial.

### RF-NUM-003
Numeración inicial:

- Venta: `V-000001`.
- Compra: `C-000001`.
- Devolución: `D-000001`.
- Anulación: `A-000001`.

### RF-NUM-004
El sistema no debe generar números duplicados para un mismo tipo de documento.

### RF-NUM-005
Los números de documentos confirmados no deben reutilizarse.

## Costeo

### RF-COST-001
Cada producto debe almacenar:

- Último costo de compra.
- Costo anterior.

### RF-COST-002
El último costo de compra será el costo actual de referencia del producto.

### RF-COST-003
Al confirmar una nueva compra, el sistema debe desplazar el último costo anterior a costo anterior y registrar el nuevo costo como último costo.

### RF-COST-004
Los detalles de compra deben conservar el costo unitario histórico utilizado en cada compra.

## Impuestos

### RF-TAX-001
El precio de venta registrado para el producto debe representar el precio final con impuesto incluido cuando corresponda.

### RF-TAX-002
El sistema debe permitir configurar el impuesto como porcentaje.

### RF-TAX-003
Cada producto debe permitir indicar si aplica impuesto o si es exento.

### RF-TAX-004
Para productos gravados, el sistema debe calcular y conservar:

- Base imponible.
- Porcentaje de impuesto.
- Monto de impuesto.

### RF-TAX-005
El impuesto no debe agregarse nuevamente al precio final de venta.

### RF-TAX-006
El porcentaje aplicado debe almacenarse en la venta para preservar el histórico.

## Operación offline del Punto de Venta

### RF-OFF-001
El formulario normal de `/pos` debe poder registrar ventas sin conexión cuando el dispositivo tenga una caja activa y datos sincronizados con una antigüedad máxima de 7 días.

### RF-OFF-002
El almacenamiento local debe limitarse a productos, último stock sincronizado, reglas de multiprecio, clientes básicos, caja activa, cuentas bancarias, configuración monetaria y ventas pendientes del POS.

### RF-OFF-003
Cada venta offline debe recibir un `ClientOperationId`, descontar el stock local y conservarse hasta que el servidor confirme la sincronización. El máximo es 500 ventas pendientes por dispositivo.

### RF-OFF-004
La sincronización debe ejecutarse al recuperar conexión, al abrir o reanudar la aplicación y mediante la acción `Sincronizar ahora`. El servidor debe ser idempotente y volver a validar todas las reglas de negocio.

### RF-OFF-005
La interfaz debe mostrar el estado de conexión, la cantidad de pendientes y el resultado o conflicto de cada operación. El comprobante local debe indicar `Pendiente de sincronización` y la numeración definitiva se asigna en el servidor.

### RF-OFF-006
Offline admite efectivo, transferencia y POS. Las ventas a crédito y las ventas con descuento requieren conexión; la aprobación de descuentos vigente no puede omitirse.

### RF-OFF-007
Compras, inventario administrativo, productos, configuración, caja administrativa, cuentas por cobrar, cuentas por pagar y demás módulos requieren servidor y no mantienen colas offline.

## Fuera del MVP

No implementar:

- Emisión y certificación FEL real mediante INFILE. La configuración segura y el almacenamiento del resultado quedan preparados, pero el conector requiere el contrato técnico del proveedor.
- Multimoneda y tipos de cambio.
- Multisucursal.
- Traslados entre sucursales.
- Cotizaciones.
- Pedidos.
- Promociones avanzadas.

## Ampliación aprobada — Control financiero y caja por usuario

- `Control` contiene los accesos administrativos `Sesiones de caja`, `Caja chica` y `Auditoría`. No se expone una opción de movimientos de efectivo. El operador de Punto de Venta conserva por separado `Mi caja` para administrar exclusivamente su sesión activa.
- La apertura normal forma parte del flujo de `Ventas > Nueva venta`; `Control > Cajas` es solo supervisión.
- Los permisos y reglas operativas se configuran como capacidades independientes y agrupadas por sección. Las autorizaciones dependen del permiso, nunca del nombre del rol.
- El cierre exige arqueo dinámico por denominaciones, conserva reconteos, separa la diferencia de efectivo de la diferencia POS y mantiene inmutable la caja cerrada.
- Los dispositivos POS son dispositivos bancarios configurables. Cada pago POS identifica el dispositivo y el cierre muestra solamente los utilizados.
- Existe una moneda global activa seleccionada desde un catálogo configurable; no se admiten multimoneda, tipos de cambio ni pagos mezclando monedas.
- Cada Caja chica es identificable, tiene un único usuario responsable y mantiene fondo, saldo, movimientos, pagos de proveedor y liquidaciones separados de las cajas de venta. Solo su responsable puede acceder y registrar movimientos; los gastos no requieren autorización adicional.
- Las compras a crédito generan cuentas por pagar con N pagos. La forma de pago y el origen del dinero son conceptos distintos; pagar desde caja de venta o Caja chica genera automáticamente el movimiento relacionado.
- Las ventas a crédito generan cuentas por cobrar con N abonos. Un abono en efectivo genera automáticamente una entrada en la caja abierta del usuario.
- Las ventas nunca se eliminan. Anulaciones y devoluciones conservan la operación original, revierten Kardex cuando corresponde y afectan únicamente una caja actualmente abierta si existe devolución física de dinero.
- `Control > Auditoría` registra únicamente trazabilidad financiera y de caja. La trazabilidad de productos permanece en `Inventario > Kardex`.
- E-commerce.
- Integraciones externas.

## Control de caducidad

- Un producto puede manejar caducidad y definir los días de anticipación para sus alertas.
- La fecha de caducidad se registra al recibir inventario y se conserva por lote.
- Productos incluye el reporte **Por caducar**, con filtros de producto, categoría, estado y rango de fechas.
