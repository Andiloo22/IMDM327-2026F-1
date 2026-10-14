// IMDM327 Material
// Use CSV or JSON to load data into the simulation. Both formats are supported, but they use different data types. 
// The CSV format uses a struct, while the JSON format uses a class. This script demonstrates how to load both formats and access their data.
using UnityEngine;
public class SolarSystemStarter: MonoBehaviour
{
    // These components can be attached independently.
    DataCSV solarCSV;
    DataJSON solarJSON;
    const float G = 6.674e-11f; // Gravitational constant
    PlanetProperty[] planetProperties;
    private int numberOfSphere = 10;
    public float TimeController = 1f;
    class PlanetProperty // why struct?
    {                   // https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/choosing-between-class-and-struct
        public GameObject planet;
        public float mass;
        public float radius;
        public Vector3 velocity;
        public Vector3 acceleration;
        public Vector3 actualPosition;
    }

    // CSV data and JSON data use their own data types.
    public BodyProperty[] solarBodiesCSV;
    // public SolarBody[] solarBodiesJSON;

    // Both loader scripts finish reading their files in Awake().
    void Start()
    {
        // CSV: use this block when a DataCSV component is attached.
        solarCSV = GetComponent<DataCSV>();
        if (solarCSV != null)
        {
            solarBodiesCSV = solarCSV.bp;
            Debug.Log("Loaded " + solarBodiesCSV.Length + " bodies from solar.csv.");
            Debug.Log("First body: mass = " + solarBodiesCSV[0].mass + ", distance = " + solarBodiesCSV[0].distance + ", initial_velocity = " + solarBodiesCSV[0].initial_velocity);
        }

        // JSON: use this block when a DataJSON component is attached.
        // solarJSON = GetComponent<DataJSON>();
        // if (solarJSON != null)
        // {
        //     solarBodiesJSON = solarJSON.solarData.bodies;
        //     Debug.Log("Loaded " + solarBodiesJSON.Length + " bodies from solar.json.");
        //     Debug.Log("First body: " + solarBodiesJSON[0].name + ", mass: " + solarBodiesJSON[0].mass);
        // }


        // GameObject array to hold the planets in the simulation.
        planetProperties = new PlanetProperty[numberOfSphere];
        for (int i = 0; i < numberOfSphere; i++)
        {
            // Our gameobjects are created here:
            planetProperties[i] = new PlanetProperty();
            planetProperties[i].planet = GameObject.CreatePrimitive(PrimitiveType.Sphere); 
        }

        // Apply the loaded data to the simulation. This is where you would set up your bodies in the scene based on the loaded data.
        for (int i = 0; i < solarBodiesCSV.Length; i++)
        {
            planetProperties[i].mass = solarBodiesCSV[i].mass;
            planetProperties[i].radius = solarBodiesCSV[i].radius;

            // What is missing here? You need to set the initial position and velocity of each planet based on the loaded data.
            // ***WRITE YOUR CODE HERE***
            // Init Position
            float initAngle = Random.Range(0f, 2f * Mathf.PI);
            float r = solarBodiesCSV[i].distance;
            Debug.Log(initAngle);
            planetProperties[i].actualPosition = new Vector3(r * Mathf.Cos(initAngle), r * Mathf.Sin(initAngle), 0f);

            // Init Velocity
            float initVelocity = solarBodiesCSV[i].initial_velocity;
            planetProperties[i].velocity = new Vector3(initVelocity * Mathf.Cos(initAngle+ Mathf.PI/2f), initVelocity * Mathf.Sin(initAngle + Mathf.PI / 2f),0f);
            // Scale
            // + This is just pretty trails
            TrailRenderer trailRenderer = planetProperties[i].planet.AddComponent<TrailRenderer>();
            // Configure the TrailRenderer's properties
            trailRenderer.time = 100.0f;  // Duration of the trail
            trailRenderer.startWidth = 0.5f;  // Width of the trail at the start
            trailRenderer.endWidth = 0.1f;    // Width of the trail at the end
            // a material to the trail
            trailRenderer.material = new Material(Shader.Find("Sprites/Default"));
            // Set the colour gradient along the trail.
            Gradient gradient = new Gradient();
            Color targetColor = Color.HSVToRGB((float)i / numberOfSphere, 1f, 1f);
            gradient.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(Color.white, 0f), // (color, normalized position)
                    new GradientColorKey(targetColor, 0.8f)
                },
                new GradientAlphaKey[] {
                    new GradientAlphaKey(1f, 0f), // (alpha, normalized position) 
                    new GradientAlphaKey(0f, 1f)
                }
            );
            trailRenderer.colorGradient = gradient;
        }
    }
    void FixedUpdate()
    {
        // Loop for N-body gravity
        // How should we design the loop?

        // 00. Initialize the acceleration for each body to zero at the start of each frame
       for (int i = 0; i < numberOfSphere; i++)
        {
            // ***WRITE YOUR CODE HERE***
            planetProperties[i].acceleration = Vector3.zero;
        }
        // 01. Loop through each body to calculate the gravitational forces acting on it
        for (int i = 0; i < numberOfSphere; i++)
        {
            for (int j = i + 1; j < numberOfSphere; j++)
            {
                Vector3 distance = planetProperties[i].actualPosition - planetProperties[j].actualPosition;
                // GRavity
                Vector3 gravity = CalculateGravity(distance, planetProperties[i].mass, planetProperties[j].mass);
                planetProperties[i].acceleration -= gravity / planetProperties[i].mass;
                planetProperties[j].acceleration += gravity / planetProperties[j].mass;
            }
        }
        // 02. Loop through each body to update its velocity and position based on the calculated acceleration
       for (int i = 0; i < numberOfSphere; i++)
        {
            // ***WRITE YOUR CODE HERE***
            planetProperties[i].velocity += planetProperties[i].acceleration * Time.fixedDeltaTime * TimeController;
            //Debug.Log(planetProperties[i].acceleration);

            planetProperties[i].actualPosition += planetProperties[i].velocity * Time.fixedDeltaTime * TimeController;

            float scaledDistance = Mathf.Sqrt(planetProperties[i].actualPosition.magnitude / 1e8f);
            planetProperties[i].planet.transform.position = scaledDistance * planetProperties[i].actualPosition.normalized;
            // Scale: float scaledDistance = Mathf.Sqrt(actualPosition[i].magnitude / 1e8f);

        }
    }

    // Gravity Fuction to finish
    private Vector3 CalculateGravity(Vector3 distanceVector, float m1, float m2)
    {
        Vector3 gravity = Vector3.zero; // note this is also Vector3
        gravity = G * m1 * m2 / (distanceVector.sqrMagnitude) * distanceVector.normalized;
        return gravity;
    }
}
