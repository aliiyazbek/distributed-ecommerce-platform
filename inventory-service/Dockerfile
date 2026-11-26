FROM node:22-alpine
WORKDIR /app/inventory-service

COPY inventory-service/package*.json ./
RUN npm ci --omit=dev

COPY inventory-service/ ./
COPY Protos/ /app/Protos/

EXPOSE 50051
CMD ["node", "server.js"]