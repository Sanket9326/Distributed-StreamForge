FROM golang:1.24.8-alpine AS build
ARG MINIO_VERSION=RELEASE.2025-10-15T17-29-55Z
RUN apk add --no-cache git
RUN CGO_ENABLED=0 go install github.com/minio/minio@${MINIO_VERSION}

FROM alpine:3.22
RUN apk add --no-cache ca-certificates wget \
    && addgroup -S minio \
    && adduser -S -G minio -h /home/minio minio \
    && mkdir -p /data \
    && chown minio:minio /data
COPY --from=build /go/bin/minio /usr/local/bin/minio
USER minio
EXPOSE 9000 9001
ENTRYPOINT ["minio"]
