FROM mono:latest
COPY server.cs .
RUN mcs -out:server.exe server.cs
EXPOSE 80
CMD mono server.exe
COPY index.html .
ADD httptest/ ./httptest/
