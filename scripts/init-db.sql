-- PostgreSQL initialization script for Dokanio POS
-- This script runs once when the PostgreSQL container is first created.
-- The application (Server) handles schema creation and data seeding via EF Core on startup.

-- Ensure the database exists (already created by POSTGRES_DB env var, but kept for clarity)
-- Grant all privileges to the application user
GRANT ALL PRIVILEGES ON DATABASE pos_multi_business TO pos_user;

-- Set timezone
SET timezone = 'UTC';
