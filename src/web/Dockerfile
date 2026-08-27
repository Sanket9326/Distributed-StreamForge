FROM node:24.15-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.29-alpine AS final
COPY --chown=101:101 nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/streamforge-web/browser /usr/share/nginx/html
EXPOSE 8080
