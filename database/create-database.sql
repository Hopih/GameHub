-- Выполнить один раз, подключившись к базе postgres от имени администратора.
-- Пароль пользователя задаётся отдельно и не хранится в этом скрипте.
CREATE ROLE gamehub_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE;
CREATE DATABASE gamehub OWNER gamehub_app ENCODING 'UTF8';
