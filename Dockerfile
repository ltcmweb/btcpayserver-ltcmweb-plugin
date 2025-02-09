FROM golang:1.23
RUN go install github.com/ltcmweb/mwebd/cmd/mwebd@latest
EXPOSE 12345
ENTRYPOINT ["/go/bin/mwebd", "-d", "/data", "-l", ":12345"]
