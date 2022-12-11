# Kubernetes YAML Files

These files are used to deploy the Nodejs app.

The Deployment uses the local Image with tag `webalerts:latest`. If the Nodejs app
was not built locally with the tag `webalerts:latest`, then the Deployment will
fail.

## Build and run the Nodejs app using Kubernetes

Follow the instructions in the [tekton/](tekton/README.md) directory to build
and run the Nodejs app using a cloud native Tekton Pipeline.

These files are automatically deployed to the cluster by the Tekton pipeline

If you do not want to use a Tekton Pipeline to build and run the app, then
execute the following in the cluster:

```bash
docker build -t webalerts:latest .
kubectl apply -f deployment/config/
```


