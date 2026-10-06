# DEVELOPMENT_PLAN.md

# Secuencia de implementación

## 1. Base
- Solución y proyectos.
- Configuración.
- Persistencia.
- Migraciones.
- Manejo de errores.
- Auditoría base.

## 2. Seguridad
- `RF-SEG-*`

## 3. Catálogo
- `RF-PROD-*`
- `RF-CAT-*`
- `RF-BAR-*`

## 4. Proveedores y compras
- `RF-PRV-*`
- `RF-COM-*`
- `RF-COST-*`

## 5. Inventario
- `RF-INV-*`

## 6. Clientes
- `RF-CLI-*`

## 7. Caja
- `RF-CAJ-*`

## 8. Punto de venta y pagos
- `RF-POS-*`
- `RF-PAY-*`
- `RF-CARD-*`
- `RF-TRF-*`
- `RF-DES-*`
- `RF-TAX-*`
- `RF-NUM-*`
- `RF-CMP-*`

## 9. Operación conectada del POS
- `RF-CON-*`

El Punto de Venta registra sus operaciones directamente en el servidor y muestra un error claro cuando no puede establecer comunicación.


## 10. Anulaciones, devoluciones y reembolsos
- `RF-ANU-*`
- `RF-DEV-*`
- `RF-REF-*`

## 11. Reportes y dashboard
- `RF-REP-*`
- `RF-DASH-*`

## 12. Configuración
- `RF-CONF-*`
- Validar configuración completa de moneda.

## 13. Validación
- Integridad.
- Autorización.
- Transacciones.
- Concurrencia.
- Rendimiento.
- Pruebas de reglas críticas.

## Pruebas del repositorio

La estructura y permanencia de los proyectos de pruebas se define en `TECH_STACK.md`.

Los proyectos de pruebas deben conservarse en Git durante todo el ciclo de desarrollo y no deben agregarse al `.gitignore`.
