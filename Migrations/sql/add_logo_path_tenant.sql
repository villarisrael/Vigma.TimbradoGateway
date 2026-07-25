-- ============================================================
-- Agrega columna logo_path a la tabla tenants
-- Vigma TimbradoGateway — Migración manual
-- Fecha: 2026-04-05
-- ============================================================

ALTER TABLE tenants
    ADD COLUMN logo_path VARCHAR(500) NULL COMMENT 'Ruta relativa del logo, ej: /logos/tenant_5.png'
    AFTER pac_produccion;
