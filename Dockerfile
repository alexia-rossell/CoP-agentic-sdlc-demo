FROM node:22-slim AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY tsconfig.json tsconfig.web.json ./
COPY src ./src
COPY evaluations ./evaluations
RUN npm run build && npm run build:web

FROM node:22-slim
ENV NODE_ENV=production \
    HOST=0.0.0.0 \
    PORT=4173 \
    COPILOT_HOME=/home/node/.copilot
WORKDIR /app
# The Copilot CLI's native HTTPS client needs the system CA store, which the slim image lacks.
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates \
    && rm -rf /var/lib/apt/lists/*
COPY package.json package-lock.json ./
RUN npm ci --omit=dev && npm cache clean --force
COPY --from=build /app/dist ./dist
COPY src/web ./src/web
COPY demo-inputs ./demo-inputs
RUN mkdir -p data /home/node/.copilot && chown -R node:node data /home/node/.copilot
USER node
EXPOSE 4173
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s \
  CMD node -e "fetch('http://127.0.0.1:'+process.env.PORT+'/api/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"
CMD ["node", "dist/src/server/main.js"]
