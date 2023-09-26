using MagicPhysX;
using MagicPhysX.Toolkit;
using static MagicPhysX.NativeMethods;
using System.Numerics;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace MagicPhysX_Test
{
    internal class Program
    {
        static Physics physics = new Physics();
        static Thread? thread;
        static bool cleaning = false;

        static void Main(string[] args)
        {
            physics.Initialize();

            Console.WriteLine("Hello, World!");

            thread = new Thread(Update);
            thread.Start();

            while (true)
            {
                string? command = Console.ReadLine();

                if (!string.IsNullOrEmpty(command))
                {
                    {
                        cleaning = true;
                        thread.Interrupt();
                        thread.Join();
                        physics.Cleanup();

                        return;
                    }
                }
            }
        }

        static void Update()
        {
            while (!cleaning)
                physics.Update();
        }

        public static void Print(string message)
        {
            Console.WriteLine(message);
        }
    }

    public unsafe class Physics
    {
        Stopwatch stopwatch = new Stopwatch();
        Stopwatch fpsStopwatch = new Stopwatch();

        Random random = new Random();

        PxFoundation* foundation;
        PxPhysics* physics;
        PxDefaultCpuDispatcher* dispatcher;
        PxScene* scene;
        PxMaterial* material;

        int fps = 0;

        public void Initialize()
        {

            foundation = physx_create_foundation();

            PxPvd* pvd = phys_PxCreatePvd(foundation);

            fixed (byte* bytePointer = "127.0.0.1"u8.ToArray())
            {
                var transport = phys_PxDefaultPvdSocketTransportCreate(bytePointer, 5425, 10);
                pvd->ConnectMut(transport, PxPvdInstrumentationFlags.All);
            }

            uint PX_PHYSICS_VERSION_MAJOR = 5;
            uint PX_PHYSICS_VERSION_MINOR = 1;
            uint PX_PHYSICS_VERSION_BUGFIX = 3;
            uint versionNumber = (PX_PHYSICS_VERSION_MAJOR << 24) + (PX_PHYSICS_VERSION_MINOR << 16) + (PX_PHYSICS_VERSION_BUGFIX << 8);

            var tolerancesScale = new PxTolerancesScale { length = 1, speed = 10 };

            physics = phys_PxCreatePhysics(versionNumber, foundation, &tolerancesScale, true, pvd, null);
            phys_PxInitExtensions(physics, pvd);

            PxSceneDesc sceneDesc = PxSceneDesc_new(PxPhysics_getTolerancesScale(physics));
            sceneDesc.gravity = new PxVec3 { x = 0f, y = -9.81f, z = 0f };

            dispatcher = phys_PxDefaultCpuDispatcherCreate(1, null, PxDefaultCpuDispatcherWaitForWorkMode.WaitForWork, 0);
            sceneDesc.cpuDispatcher = (PxCpuDispatcher*)dispatcher;
            sceneDesc.filterShader = get_default_simulation_filter_shader();

            scene = physics->CreateSceneMut(&sceneDesc);
            material = physics->CreateMaterialMut(0.5f, 0.5f, 0.6f);

            scene->GetScenePvdClientMut()->SetScenePvdFlagMut(PxPvdSceneFlag.TransmitScenequeries, true);

            var pvdClient = scene->GetScenePvdClientMut();
            if (pvdClient != null)
            {
                pvdClient->SetScenePvdFlagMut(PxPvdSceneFlag.TransmitConstraints, true);
                pvdClient->SetScenePvdFlagMut(PxPvdSceneFlag.TransmitContacts, true);
                pvdClient->SetScenePvdFlagMut(PxPvdSceneFlag.TransmitScenequeries, true);
            }

            PxPlane plane = PxPlane_new_1(0f, 1f, 0f, 0f);
            PxRigidStatic* groundPlane = physics->PhysPxCreatePlane(&plane, material);
            scene->AddActorMut((PxActor*)groundPlane, null);

            PxSphereGeometry sphereGeo = PxSphereGeometry_new(0.4f);
            PxVec3 zero = new PxVec3 { x = 0f, y = 0f, z = 0f };
            PxTransform shapeOffset = PxTransform_new_1(&zero);

            for (int i = 0; i < 100; i++)
            {
                //PxVec3 vec3 = new PxVec3 { x = random.NextSingle() * 10f, y = random.NextSingle() * 10f, z = random.NextSingle() * 10f };
                PxVec3 vec3 = new PxVec3 { x = 0f, y = 1f, z = -50 + i * 1f };
                PxTransform transform = PxTransform_new_1(&vec3);
                PxTransform identity = PxTransform_new_2(PxIDENTITY.PxIdentity);
                PxRigidStatic* sphere = physics->PhysPxCreateStatic(&transform, (PxGeometry*)&sphereGeo, material, &shapeOffset);
                //PxRigidBody_setAngularDamping_mut((PxRigidBody*)sphere, 0.5f);
                scene->AddActorMut((PxActor*)sphere, null);
            }

            for (int i = 0; i < 10; i++)
            {
                PxVec3 vec3 = new PxVec3 { x = random.NextSingle() * 10f, y = random.NextSingle() * 10f, z = random.NextSingle() * 10f };
                //PxVec3 vec3 = new PxVec3 { x = 0f, y = 1f, z = -50 + i * 1f };
                PxTransform transform = PxTransform_new_1(&vec3);
                PxTransform identity = PxTransform_new_2(PxIDENTITY.PxIdentity);
                //PxRigidStatic* sphere = physics->PhysPxCreateStatic(&transform, (PxGeometry*)&sphereGeo, material, &shapeOffset);
                //PxRigidBody_setAngularDamping_mut((PxRigidBody*)sphere, 0.5f);
                PxRigidDynamic* sphere = physics->PhysPxCreateDynamic(&transform, (PxGeometry*)&sphereGeo, material, 10f, &shapeOffset);
                scene->AddActorMut((PxActor*)sphere, null);
            }

            stopwatch.Start();
            fpsStopwatch.Start();
        }

        public void Cleanup()
        {
            PxScene_release_mut(scene);
            PxDefaultCpuDispatcher_release_mut(dispatcher);
            PxPhysics_release_mut(physics);
        }

        public void Update()
        {
            fps++;

            if (fpsStopwatch.Elapsed.TotalSeconds >= 1d)
            {
                Program.Print($"FPS : {fps}");

                fps = 0;
                fpsStopwatch.Restart();

                RaycastTest();
            }

            if (stopwatch.Elapsed.TotalSeconds < 0.02d)
                return;

            float delta = (float)stopwatch.Elapsed.TotalSeconds;
            stopwatch.Restart();

            scene->SimulateMut(delta, null, null, 0, true);
            uint error = 0;
            scene->FetchResultsMut(true, &error);
        }

        public void RaycastTest()
        {
            //PxVec3 origin = new PxVec3 { x = random.NextSingle() * 10f, y = random.NextSingle() * 10f, z = random.NextSingle() * 10f };
            //PxVec3 direction = new PxVec3 { x = random.NextSingle() * 10f, y = random.NextSingle() * 10f, z = random.NextSingle() * 10f };
            //PxVec3 origin = new PxVec3 { x = 0f, y = 1f, z = -55f };
            PxVec3 origin = new PxVec3 { x = -1f, y = 1f, z = 0f };
            PxVec3 direction = new PxVec3 { x = random.NextSingle(), y = 0f, z = random.NextSingle() };
            //PxVec3 direction = new PxVec3 { x = 0f, y = 0f, z = 1f };
            direction = direction.GetNormalized();

            PxHitFlags outputFlags1 = PxHitFlags.Position;
            PxQueryFilterData filterData1 = PxQueryFilterData_new();
            //filterData1.flags = PxQueryFlags.NoBlock;

            PxRaycastHit[] hitInfo = new PxRaycastHit[128];
            bool block = true;
            void* blockPtr = &block;

            fixed (PxRaycastHit* hitInfoPtr = &hitInfo[0])
            {
                int result1 = scene->QueryExtRaycastMultiple((PxVec3*)Unsafe.AsPointer(ref origin), (PxVec3*)Unsafe.AsPointer(ref direction), 128f, outputFlags1, hitInfoPtr, 128u, (bool*)blockPtr, &filterData1, null, null);
                //bool result1 = scene->QueryExtRaycastSingle((PxVec3*)Unsafe.AsPointer(ref origin), (PxVec3*)Unsafe.AsPointer(ref direction), 100f, outputFlags1, &hitInfo, &filterData1, null, null);
                Program.Print($"Raycast result1: {result1}");
                Program.Print($"Block: {block}");

                for (int i = 0; i < result1; i++)
                {
                    PxRaycastHit hit = hitInfo[i];
                    Program.Print($"[{i}]    hit.position=x:{hit.position.x}\ty:{hit.position.y}\tz:{hit.position.z}");

                    if (i > 0)
                    {
                        PxRaycastHit prvHit = hitInfo[i - 1];

                        if (prvHit.position.x == hit.position.x && prvHit.position.y == hit.position.y && prvHit.position.z == hit.position.z)
                        {
                            Program.Print("");
                            Program.Print($"Same Position !!!!!!!!!!!!!!!!!!!");
                            Program.Print("");
                            Thread.Sleep(5000);
                        }
                    }
                }
            }
        }
    }
}